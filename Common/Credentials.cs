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

    /// <summary>
    /// Authorization code (and granted scope) returned by GPALRESTAPI's get-access-token endpoint after a user completes an OAuth redirect.
    /// </summary>
    public class AuthCodePayload
    {
        /// <summary>
        /// OAuth authorization code to exchange for an access token.
        /// </summary>
        public string Code { get; set; }
        /// <summary>
        /// OAuth scope(s) granted along with the authorization code.
        /// </summary>
        public string Scope { get; set; }

        public AuthCodePayload()
        { }
    }
    /// <summary>
    /// Deserialized contents of a Google Cloud service account JSON key file, used by GoogleHandler.FetchServiceAccountToken to sign a JWT.
    /// </summary>
    public class GoogleServiceAccountKey
    {
        /// <summary>
        /// Key type, normally "service_account".
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// Google Cloud project ID that owns this service account.
        /// </summary>
        public string ProjectId { get; set; }
        /// <summary>
        /// Identifier of the private key within the service account.
        /// </summary>
        public string PrivateKeyId { get; set; }
        /// <summary>
        /// PEM-encoded RSA private key used to sign the JWT assertion.
        /// </summary>
        public string PrivateKey { get; set; }
        /// <summary>
        /// Email address of the service account; used as the JWT issuer (iss claim).
        /// </summary>
        public string ClientEmail { get; set; }
        /// <summary>
        /// OAuth client ID associated with the service account.
        /// </summary>
        public string ClientId { get; set; }
        /// <summary>
        /// Google OAuth authorization endpoint URL.
        /// </summary>
        public string AuthUri { get; set; }
        /// <summary>
        /// Google OAuth token endpoint URL used to exchange the signed JWT for an access token.
        /// </summary>
        public string TokenUri { get; set; }
        /// <summary>
        /// URL of Google's X.509 certificate provider for verifying tokens.
        /// </summary>
        public string AuthProviderX509CertUrl { get; set; }
        /// <summary>
        /// URL of this service account's X.509 certificate.
        /// </summary>
        public string ClientX509CertUrl { get; set; }

        public GoogleServiceAccountKey() { }
    }

    /// <summary>
    /// JSON response from Bitwarden's OAuth token endpoint.
    /// </summary>
    public class BitwardenTokenResponse
    {
        /// <summary>
        /// Bearer access token used to call the Bitwarden vault API.
        /// </summary>
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Number of seconds until AccessToken expires.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token used to obtain a new access token once it expires.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// OAuth token type, normally "Bearer".
        /// </summary>
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }


    /// <summary>
    /// JSON response from the Bitwarden vault items search endpoint.
    /// </summary>
    internal class BitwardenVaultResponse
    {
        /// <summary>
        /// Vault items matching the search.
        /// </summary>
        public List<BitwardenVaultItem> Data { get; set; }
    }

    /// <summary>
    /// A single item in a Bitwarden vault response.
    /// </summary>
    internal class BitwardenVaultItem
    {
        /// <summary>
        /// Login details for this vault item, or null if it is not a login item.
        /// </summary>
        public BitwardenLogin Login { get; set; }
    }

    /// <summary>
    /// Login fields (username, password, and associated site URLs) of a Bitwarden vault item.
    /// </summary>
    internal class BitwardenLogin
    {
        /// <summary>
        /// Stored username/login.
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Stored password.
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// URLs associated with this login, used for matching against .WithDomain.
        /// </summary>
        public List<BitwardenUri> Uris { get; set; }
    }

    /// <summary>
    /// A single URL associated with a Bitwarden login item.
    /// </summary>
    internal class BitwardenUri
    {
        /// <summary>
        /// The URL string.
        /// </summary>
        public string Uri { get; set; }
    }

    /// <summary>
    /// JSON response from Google's OAuth token endpoint.
    /// </summary>
    public class GoogleTokenResponse
    {
        /// <summary>
        /// Bearer access token for calling Google APIs.
        /// </summary>
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Number of seconds until AccessToken expires.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token used to obtain a new access token once it expires. Only returned on the initial authorization code exchange.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// OAuth token type, normally "Bearer".
        /// </summary>
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }

    /// <summary>
    /// JSON response from the Azure AD (Microsoft Entra ID) OAuth token endpoint.
    /// </summary>
    public class AzureTokenResponse
    {
        /// <summary>
        /// Bearer access token for calling Microsoft Graph or other Azure APIs.
        /// </summary>
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Number of seconds until AccessToken expires.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token used to obtain a new access token once it expires.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// Space-delimited list of scopes granted to AccessToken.
        /// </summary>
        [JsonPropertyName("scope")]
        public string Scope { get; set; }

        /// <summary>
        /// OAuth token type, normally "Bearer".
        /// </summary>
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }

    /// <summary>
    /// JSON response from the AWS Cognito OAuth token endpoint.
    /// </summary>
    public class AWSTokenResponse
    {
        /// <summary>
        /// Bearer access token for calling AWS APIs.
        /// </summary>
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Number of seconds until AccessToken expires.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token used to obtain a new access token once it expires. Cognito may not always return one.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// OAuth token type, normally "Bearer".
        /// </summary>
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        /// <summary>
        /// OpenID Connect ID token containing identity claims for the authenticated user.
        /// </summary>
        [JsonPropertyName("id_token")]
        public string IdToken { get; set; }
    }

    /// <summary>
    /// A single vault item returned by the LastPass CLI (`lpass ls --json`).
    /// </summary>
    public class LastPassItem
    {
        /// <summary>
        /// Unique identifier of the vault item.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Display name of the vault item.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// URL associated with the vault item, used for matching against .WithDomain.
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// Stored username/login.
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Stored password.
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// Free-form notes attached to the vault item.
        /// </summary>
        public string Notes { get; set; }
    }

    /// <summary>
    /// A single vault item returned by the 1Password CLI (`op item list --format=json`).
    /// </summary>
    public class OnePasswordItem
    {
        /// <summary>
        /// Unique identifier of the vault item.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Display title of the vault item.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Name or ID of the 1Password vault containing this item.
        /// </summary>
        public string Vault { get; set; }           // vault name or id
        /// <summary>
        /// Custom fields (e.g. username, password) belonging to this item.
        /// </summary>
        public List<OnePasswordField> Fields { get; set; } = new List<OnePasswordField>();
        /// <summary>
        /// URLs associated with this item, used for matching against .WithDomain.
        /// </summary>
        public List<OnePasswordUrl> Urls { get; set; }
    }

    /// <summary>
    /// A single URL associated with a 1Password item.
    /// </summary>
    public class OnePasswordUrl
    {
        /// <summary>
        /// The URL string.
        /// </summary>
        public string Href { get; set; }
    }

    /// <summary>
    /// A single custom field of a 1Password item, such as a username or password field.
    /// </summary>
    public class OnePasswordField
    {
        /// <summary>
        /// Unique identifier of the field within the item.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Display label of the field (e.g. "username", "password").
        /// </summary>
        public string Label { get; set; }
        /// <summary>
        /// Field type as reported by the 1Password CLI (e.g. "STRING", "CONCEALED").
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// Field value, when not a secret reference.
        /// </summary>
        public string Value { get; set; }
        /// <summary>
        /// secret reference URI (op://...) used to resolve the value for secret fields.
        /// </summary>
        public string Reference { get; set; }       // for secrets that are references
    }

    /// <summary>
    /// A single vault item returned by the Dashlane CLI (`dcli password list --json`).
    /// </summary>
    public class DashlaneItem
    {
        /// <summary>
        /// Unique identifier of the vault item.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Display title of the vault item.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// URL associated with the vault item, used for matching against .WithDomain.
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// Stored username/login.
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Stored password.
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// One-time-password/TOTP secret, if configured for the item.
        /// </summary>
        public string Otp { get; set; }
        /// <summary>
        /// Free-form notes attached to the vault item.
        /// </summary>
        public string Notes { get; set; }
    }

    /// <summary>
    /// A single record returned by Keeper Commander (`keeper list`/`keeper search --format=json`).
    /// </summary>
    public class KeeperRecord
    {
        /// <summary>
        /// Unique identifier of the Keeper record.
        /// </summary>
        public string RecordUid { get; set; }
        /// <summary>
        /// Display title of the record.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Keeper record type (e.g. "login").
        /// </summary>
        public string RecordType { get; set; }
        /// <summary>
        /// Login fields (username, password, URL) of the record, if it is a login record.
        /// </summary>
        public KeeperLoginData Login { get; set; }
        /// <summary>
        /// Free-form notes attached to the record.
        /// </summary>
        public string Notes { get; set; }
    }

    /// <summary>
    /// Login fields (username, password, and URL) of a Keeper record.
    /// </summary>
    public class KeeperLoginData
    {
        /// <summary>
        /// Stored username/login.
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Stored password.
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// URL associated with the login, used for matching against .WithDomain.
        /// </summary>
        public string Url { get; set; }
    }

    [InProgress("Credential class to get access tokens and credentials from password managers. Untested. 50%")]
    public class Credentials : ICredentials
    {
        /// <summary>
        /// Password manager or cloud identity service this credentials request targets, set via .WithService.
        /// </summary>
        internal CredentialServiceType ServiceType { get; private set; }
        /// <summary>
        /// Username supplied via .WithUsername, or the username field returned from a vault item.
        /// </summary>
        public string Username { get; private set; }
        /// <summary>
        /// Password supplied via .WithPassword, or the password field returned from a vault item.
        /// </summary>
        internal string Password { get; private set; }
        /// <summary>
        /// Combined "ClientId:ClientSecret" service key supplied via .WithServiceKey or derived from .WithClientId/.WithClientSecret.
        /// </summary>
        public string ServiceKey { get; private set; }
        /// <summary>
        /// The site's authentication scheme (Basic, Digest, Proxy, Bearer, Form), set via .WithWebAuth. A browser reads
        /// it to decide whether it can send the credential before the first request.
        /// </summary>
        public WebAuthType WebAuthType { get; private set; }
        /// <summary>
        /// RESTClient supplied via .WithKeyFromApi, used by the following .WithEndpoint(endpoint) call to fetch the service key.
        /// </summary>
        private IRESTClient _keyFromApiRestClient;
        /// <summary>
        /// OAuth client ID supplied via .WithClientId or derived from .WithServiceKey.
        /// </summary>
        public string ClientId { get; internal set; }
        /// <summary>
        /// OAuth client secret supplied via .WithClientSecret or derived from .WithServiceKey.
        /// </summary>
        public string ClientSecret { get; internal set; }
        /// <summary>
        /// Most recently fetched access token for ServiceType, set by FetchAccessToken/GetCredentialsFor.
        /// </summary>
        public string AccessToken { get; internal set; }
        /// <summary>
        /// Search term supplied via GetCredentialsFor, used by the password manager handlers to find a matching vault item.
        /// </summary>
        public string Target { get; private set; }
        /// <summary>
        /// Optional URL filter supplied via .WithDomain, used to narrow vault matches down to a specific site.
        /// </summary>
        public string Domain { get; private set; }
        /// <summary>
        /// Endpoint/protocol configuration for the credential service handlers, loaded from ./credentialsConfig.json.
        /// </summary>
        public CredentialsConfig Config { get; private set; }
        /// <summary>
        /// OAuth authorization code supplied via .WithAuthCode, or retrieved automatically during the OAuth redirect flow.
        /// </summary>
        public string AuthCode { get; set; }
        /// <summary>
        /// OAuth refresh token supplied via .WithRefreshToken, or returned by a token exchange and used to renew AccessToken.
        /// </summary>
        public string RefreshToken { get; set; }
        /// <summary>
        /// OAuth scope requested via .WithScope when fetching an access token.
        /// </summary>
        public OAuthScope OAuthScope { get; set; }
        /// <summary>
        /// Most recent token response from Google's OAuth token endpoint.
        /// </summary>
        internal GoogleTokenResponse GoogleTokenResponse { get; set; }
        /// <summary>
        /// Most recent token response from the AWS Cognito OAuth token endpoint.
        /// </summary>
        internal AWSTokenResponse AWSTokenResponse { get; set; }
        /// <summary>
        /// Most recent token response from the Azure AD OAuth token endpoint.
        /// </summary>
        internal AzureTokenResponse AzureTokenResponse { get; set; }
        /// <summary>
        /// Most recent token response from the Bitwarden identity service.
        /// </summary>
        internal BitwardenTokenResponse BitwardenTokenResponse { get; set; }
        /// <summary>
        /// Raw JSON contents of a Google service account key, supplied via .WithServiceAccountKey.
        /// </summary>
        internal string ServiceAccountKeyJson { get; set; }
        /// <summary>
        /// TOTP secret supplied via .WithOtpSecret: the base32 key, or the otpauth:// URI that holds it.
        /// </summary>
        internal string OtpSecret { get; private set; }
        /// <summary>
        /// UTC time at which AccessToken was issued, used together with ExpiresIn to compute ExpiresAt.
        /// </summary>
        internal DateTime IssueTime { get; set; }
        private int _expiresIn = 0;
        /// <summary>
        /// Lifetime of AccessToken in seconds, as reported by the token endpoint. Setting this also recomputes ExpiresAt.
        /// </summary>
        internal int ExpiresIn
        {
            get => _expiresIn;
            set
            {
                _expiresIn = value;
                ExpiresAt = DateTime.UtcNow.AddSeconds(_expiresIn);
            }
        }
        /// <summary>
        /// UTC time at which AccessToken expires, computed from IssueTime and ExpiresIn.
        /// </summary>
        internal DateTime ExpiresAt { get; set; }
        /// <summary>
        /// Browser instance to use when launching a browser for the manual OAuth redirect flow.
        /// </summary>
        internal Browser.Browser Browser { get; set; }

        // =======================================================
        // HELPERS
        // =======================================================
        /// <summary>
        /// Splits a "left:right" string into its two parts, e.g. a "ClientId:ClientSecret" service key.
        /// </summary>
        /// <param name="value">String to split on the first colon.</param>
        /// <returns>A tuple of (left, right); either or both may be empty if value is null/empty or has no colon.</returns>
        internal static (string, string) SafeSplit(string value)
        {
            if (string.IsNullOrEmpty(value)) return ("", "");
            var parts = value.Split(new[] { ':' }, 2);
            return (parts[0], parts.Length > 1 ? parts[1] : "");
        }

        /// <summary>
        /// Returns true if no domain filter is set (.WithDomain was not called), or if the given URL contains the domain.<br/>
        /// Used by the password manager handlers to narrow vault matches down to a specific site.
        /// </summary>
        internal static bool MatchesDomain(string url, string domain)
        {
            if (string.IsNullOrEmpty(domain)) return true;
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf(domain, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true if no domain filter is set, or if any of the given URLs contains the domain.
        /// </summary>
        internal static bool MatchesDomain(IEnumerable<string> urls, string domain)
        {
            if (string.IsNullOrEmpty(domain)) return true;
            if (urls == null) return false;
            return urls.Any(url => MatchesDomain(url, domain));
        }

        /// <summary>
        /// Returns a valid access token, fetching a new one via FetchAccessToken if AccessToken is missing or expired.
        /// </summary>
        /// <param name="accessToken">Receives a current access token for ServiceType.</param>
        private void EnsureValidToken(out string accessToken)
        {
            if (IsTokenExpired())
                FetchAccessToken(out accessToken);
            else
                accessToken = AccessToken;
        }
        /// <summary>
        /// Maps an OAuthScope value to its provider-specific scope URL/string.
        /// </summary>
        /// <param name="scope">Scope to resolve.</param>
        /// <returns>The scope URL/string to send to the OAuth provider, or an empty string if unsupported.</returns>
        internal static string GetScopeValue(OAuthScope scope)
        {
            switch (scope)
            {
                // Google
                case OAuthScope.Google_Sheets:
                    return "https://www.googleapis.com/auth/spreadsheets";
                case OAuthScope.Google_Drive:
                    return "https://www.googleapis.com/auth/drive";
                case OAuthScope.Google_CloudPlatform:
                    return "https://www.googleapis.com/auth/cloud-platform";
                case OAuthScope.YouTube_Upload:
                    return "https://www.googleapis.com/auth/youtube.upload";
                case OAuthScope.YouTube_Manage:
                    return "https://www.googleapis.com/auth/youtube";

                // Azure
                case OAuthScope.Azure_GraphUserRead:
                    return "https://graph.microsoft.com/User.Read";
                case OAuthScope.Azure_GraphMailRead:
                    return "https://graph.microsoft.com/Mail.Read";
                case OAuthScope.Azure_Storage:
                    return "https://storage.azure.com/user_impersonation";
                case OAuthScope.Azure_Management:
                    return "https://management.azure.com/user_impersonation";

                // AWS
                case OAuthScope.AWS_CognitoOpenId:
                    return "openid";
                case OAuthScope.AWS_CognitoProfile:
                    return "profile";
                case OAuthScope.AWS_APIGatewayCustom:
                    return "myapp/read"; // your custom

                default:
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unsupported scope: [{scope}]", scope, GPALObjectType.Other);
                    return string.Empty;
            }
        }

        // =======================================================
        // FLUENT interface
        // =======================================================
        /// <summary>
        /// Sets the OAuth scope to request when an access token is fetched.
        /// </summary>
        /// <param name="scope">Scope to request.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithScope(OAuthScope scope)
        {
            OAuthScope = scope;
            return this;
        }
        /// <summary>
        /// Supplies a previously-obtained OAuth authorization code, skipping the interactive authorization step.
        /// </summary>
        /// <param name="authCode">Authorization code returned by the OAuth provider's consent screen.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithAuthCode(string authCode)
        {
            AuthCode = authCode;
            return this;
        }
        /// <summary>
        /// Supplies a previously-obtained OAuth refresh token, used to fetch a new access token without re-authorizing.
        /// </summary>
        /// <param name="refreshToken">Refresh token returned by a prior token exchange.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithRefreshToken(string refreshToken)
        {
            RefreshToken = refreshToken;
            return this;
        }

        internal Credentials(Browser.Browser browser)
        {
            Config = CredentialsConfig.Load();
            Browser = browser;
        }

        /// <summary>
        /// Selects which password manager or cloud identity service this credentials request targets.
        /// </summary>
        /// <param name="serviceType">Service to query/store credentials with, or CredentialServiceType.None for username/password supplied directly.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialMethod WithService(CredentialServiceType serviceType)
        {
            ServiceType = serviceType;
            return this;
        }

        /// <summary>
        /// Sets the username, either as a credential to retrieve directly (with CredentialServiceType.None) or as part of WithCredentials' master login.
        /// </summary>
        /// <param name="username">Username/login value.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialsPassword WithUsername(string username)
        {
            Username = username;
            return this;
        }

        /// <summary>
        /// Sets the password, either as a credential to retrieve directly (with CredentialServiceType.None) or as part of WithCredentials' master login.
        /// </summary>
        /// <param name="password">Password value.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowOAuthClient WithPassword(string password)
        {
            Password = password;
            return this;
        }

        /// <summary>
        /// Sets the site's authentication scheme; a browser given this credential applies it.
        /// </summary>
        /// <param name="webAuthType">Basic, Digest, Proxy or Bearer; Form when the workflow fills in the login page itself and only holds the credential</param>
        /// <returns>Fluent interface to define more credential settings</returns>
        public IAllowOAuthClient WithWebAuth(WebAuthType webAuthType)
        {
            WebAuthType = webAuthType;
            return this;
        }

        /// <summary>
        /// Sets the OAuth redirect URI, which must be the one registered with the provider. GPAL listens on its host
        /// and port, or uses a GPALRestAPI already there.<br/>
        /// Defaults to the value in credentialsConfig.json, http://localhost:3000/access-token out of the box.
        /// </summary>
        /// <param name="redirectUrl">The redirect URI as registered with the provider, e.g. http://localhost:3117/access-token</param>
        /// <returns>Fluent interface to define more credential settings</returns>
        public IAllowOAuthClient WithAuthRedirectUrl(string redirectUrl)
        {
            Config.GoogleRestRedirectUri = redirectUrl;
            return this;
        }

        /// <summary>
        /// Sets an existing access token for the Bearer, X-Auth-Token, X-API-Key and api-key schemes. It is sent the
        /// same way as a token GPAL fetched.
        /// </summary>
        /// <param name="accessToken">The token as the site issued it, without a scheme word such as "Bearer" in front</param>
        /// <returns>Fluent interface to define more credential settings</returns>
        public IAllowOAuthClient WithAccessToken(string accessToken)
        {
            AccessToken = accessToken;
            return this;
        }

        /// <summary>
        /// Restricts GetCredentialsFor/SaveTo results to vault items whose URL contains this domain. If not called, no domain filtering is applied.
        /// </summary>
        /// <param name="domain">Domain or URL fragment to match against vault item URLs.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithDomain(string domain)
        {
            Domain = domain;
            return this;
        }
        /// <summary>
        /// Sets the OAuth client ID used to authenticate to ServiceType.
        /// </summary>
        /// <param name="clientId">OAuth client ID.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowClientSecret WithClientId(string clientId)
        {
            ClientId = clientId;
            return this;
        }
        /// <summary>
        /// Sets the OAuth client secret and derives ServiceKey as "ClientId:ClientSecret".
        /// </summary>
        /// <param name="clientSecret">OAuth client secret.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithClientSecret(string clientSecret)
        {
            ClientSecret = clientSecret;
            ServiceKey = $"{ClientId}:{clientSecret}";
            return this;
        }
        /// <summary>
        /// Service key is a custom string consisting of your ClientID:ClientSecret
        /// </summary>
        /// <param name="key">Combined "ClientId:ClientSecret" service key.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithServiceKey(string key)
        {
            ServiceKey = key;
            var parts = SafeSplit(key);
            ClientId = parts.Item1;
            ClientSecret = parts.Item2;
            return this;
        }

        /// <summary>
        /// Like WithServiceKey, but reads the key from an environment variable (checking process,
        /// user, then machine scope) instead of taking it as a literal string.
        /// </summary>
        /// <param name="envVariableName">Name of the environment variable holding the API key.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithKeyFromEnv(string envVariableName)
        {
            string key = Environment.GetEnvironmentVariable(envVariableName, EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable(envVariableName, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(envVariableName, EnvironmentVariableTarget.Machine);

            if (string.IsNullOrEmpty(key))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Environment variable [{envVariableName}] is not set or empty.",
                    null,
                    GPALObjectType.None);
                return this;
            }

            return WithServiceKey(key);
        }

        /// <summary>
        /// Like WithServiceKey, but fetches the key via a GET request made with the supplied, already-configured RESTClient.
        /// Must be followed by .WithEndpoint(endpoint) to specify which endpoint returns the key,
        /// e.g. GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithKeyFromApi(restClient).WithEndpoint("ANTKEY").ToGPALObject()
        /// </summary>
        /// <param name="restClient">Already-configured RESTClient (e.g. via .WithAPIBase(...)) used to fetch the key.</param>
        /// <returns>Fluent interface that only allows .WithEndpoint(endpoint) next.</returns>
        public IAllowKeyFromApiEndpoint WithKeyFromApi(IRESTClient restClient)
        {
            _keyFromApiRestClient = restClient;
            return this;
        }

        /// <summary>
        /// Completes .WithKeyFromApi(restClient) by calling the given endpoint on that RESTClient and using the
        /// returned value as the service key (see WithServiceKey).
        /// </summary>
        /// <param name="endpoint">REST endpoint name to call for the key, e.g. "ANTKEY".</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithEndpoint(string endpoint)
        {
            string key = _keyFromApiRestClient
                .WithEndpoint(endpoint)
                .Execute();

            if (string.IsNullOrEmpty(key))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"REST API returned no key for endpoint [{endpoint}].",
                    null,
                    GPALObjectType.None);
                return this;
            }

            return WithServiceKey(key);
        }

        /// <summary>
        /// Loads a Google Cloud service account key from a JSON key file, used to fetch an access token without an interactive OAuth flow.
        /// </summary>
        /// <param name="serviceAccountKeyFile">GPALFile pointing at the service account JSON key file.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithServiceAccountKey(GPALFile serviceAccountKeyFile)
        {
            if (serviceAccountKeyFile == null || string.IsNullOrWhiteSpace(serviceAccountKeyFile.Filename) || !File.Exists(serviceAccountKeyFile.Filename))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Service account key file missing or not found: [{serviceAccountKeyFile?.Filename ?? "<null>"}]",
                    null,
                    GPALObjectType.None);
                return this;
            }

            ServiceAccountKeyJson = File.ReadAllText(serviceAccountKeyFile.Filename);
            return this;
        }

        /// <summary>
        /// Supplies a Google Cloud service account key as a raw JSON string, used to fetch an access token without an interactive OAuth flow.
        /// </summary>
        /// <param name="serviceAccountKey">Service account key JSON contents.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithServiceAccountKey(string serviceAccountKey)
        {
            if (string.IsNullOrWhiteSpace(serviceAccountKey))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Service account key JSON string is empty.",
                    null,
                    GPALObjectType.None);
                return this;
            }

            ServiceAccountKeyJson = serviceAccountKey;
            return this;
        }

        /// <summary>
        /// Supplies a Google Cloud service account key as a deserialized object, used to fetch an access token without an interactive OAuth flow.
        /// </summary>
        /// <param name="serviceAccountKey">Service account key object, normally loaded from a JSON key file.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithServiceAccountKey(GoogleServiceAccountKey serviceAccountKey)
        {
            if (serviceAccountKey == null)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Service account key object is null.",
                    null,
                    GPALObjectType.None);
                return this;
            }

            try
            {
                ServiceAccountKeyJson = System.Text.Json.JsonSerializer.Serialize(serviceAccountKey);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Failed to serialize service account key object",
                    null,
                    GPALObjectType.None, ex);
            }
            return this;
        }

        /// <summary>
        /// Supplies the login that unlocks the password manager (Bitwarden, LastPass, 1Password).
        /// </summary>
        /// <remarks>
        /// Copies only the authentication fields; the service is still the one WithService named.
        /// </remarks>
        /// <param name="masterCredentials">A credentials object holding the login for the password manager</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithMasterCredentials(ICredentials masterCredentials)
        {
            if (masterCredentials is Credentials master)
            {
                Username = master.Username;
                Password = master.Password;
                ServiceKey = master.ServiceKey;
                ClientId = master.ClientId;
                ClientSecret = master.ClientSecret;
                ServiceAccountKeyJson = master.ServiceAccountKeyJson;
                AccessToken = master.AccessToken;
                RefreshToken = master.RefreshToken;
                OAuthScope = master.OAuthScope;
            }

            return this;
        }

        /// <summary>
        /// Sets the secret a site gives out when two-factor sign-in is turned on, which GetOtpCode turns into the
        /// current 6-digit code. It is the base32 key behind the site's "can't scan the code?" link, or the whole
        /// otpauth://totp/ URI the setup QR code holds.
        /// </summary>
        /// <param name="otpSecret">Base32 key such as JBSWY3DPEHPK3PXP, or an otpauth://totp/ URI</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithOtpSecret(string otpSecret)
        {
            OtpSecret = otpSecret;
            return this;
        }

        /// <summary>
        /// Reads the one-time password secret from a picture of a QR code: the setup code a site shows when
        /// two-factor sign-in is turned on, or a Google Authenticator export, which holds every exported account.
        /// Needs the GPAL.Image package, registered with GPALImage.Register().
        /// </summary>
        /// <param name="qrCodeImage">A picture with the QR code on it, such as a saved screenshot</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithOtpQRCode(GPALFile qrCodeImage)
        {
            if (null == qrCodeImage || false == File.Exists(qrCodeImage.Filename))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The QR code picture [{qrCodeImage?.Filename}] was not found.", null, GPALObjectType.Other);
            }
            else
            {
                using (Bitmap image = new Bitmap(qrCodeImage.Filename))
                {
                    // a phone photo is stored as the sensor saw it, with its EXIF orientation tag (0x0112) saying how
                    // to turn it upright; Bitmap does not apply the tag
                    int orientation = image.PropertyIdList.Contains(0x0112) ? image.GetPropertyItem(0x0112).Value[0] : 1;
                    if (3 == orientation)
                        image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    else if (6 == orientation)
                        image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (8 == orientation)
                        image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    ReadOtpQRCode(image, qrCodeImage.Filename);
                }
            }
            return this;
        }

        /// <summary>
        /// Reads the one-time password secret from a QR code the browser is showing, such as a site's two-factor
        /// setup page. Needs the GPAL.Image package, registered with GPALImage.Register().
        /// </summary>
        /// <param name="browser">The browser showing the QR code</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowCredentialSettings WithOtpQRCode(IBrowser browser)
        {
            GPAL.ImageHelper.CaptureScreen(browser).ToBitmap(out Bitmap image);
            ReadOtpQRCode(image, "the browser");
            return this;
        }

        /// <summary>
        /// Has the registered QR code reader read the picture and keeps what it holds as the one-time password secret.
        /// </summary>
        /// <param name="image">The picture with the QR code</param>
        /// <param name="source">Where the picture came from, for the messages</param>
        private void ReadOtpQRCode(Bitmap image, string source)
        {
            if (null == GPAL.QRCodeReader)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Reading a QR code needs the GPAL.Image package, added and registered with GPALImage.Register() at startup, so [{source}] was not read.", null, GPALObjectType.Other);
            }
            else
            {
                string text = GPAL.QRCodeReader.ReadQRCode(image);
                if (false == text.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase))
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No one-time password QR code was found on [{source}].", null, GPALObjectType.Other);
                else
                {
                    OtpSecret = text;
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Read the one-time password QR code on [{source}], holding [{OtpHandler.ReadAccounts(text).Count}] accounts.", null, GPALObjectType.Other);
                }
            }
        }

        /// <summary>
        /// Makes the one-time code an authenticator app would show right now for the secret set with .WithOtpSecret
        /// or .WithOtpQRCode. When that secret is an export holding several accounts, .GetCredentialsFor names the
        /// one to use by its issuer or account name. A new code comes every 30 seconds unless the URI says otherwise.
        /// </summary>
        /// <param name="code">Receives the code, or an empty string if no secret was set or it could not be read</param>
        public void GetOtpCode(out string code)
        {
            code = "";
            OtpAccount account = OtpHandler.ChooseAccount(OtpHandler.ReadAccounts(OtpSecret), Target);
            if (null != account)
            {
                code = OtpHandler.GenerateCode(account.Uri, DateTime.UtcNow, out DateTime validUntil);
                if (false == string.IsNullOrEmpty(code))
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Made the one-time code for [{account.Issuer}], current until [{validUntil.ToLocalTime():HH:mm:ss}].", null, GPALObjectType.Other);
            }
        }

        /// <summary>
        /// Appends an [Issuer, Account, otpauth URI] row to the grid for each account the one-time password secret
        /// holds, so the secrets from a Google Authenticator export can be kept and each one given to WithOtpSecret.
        /// The URI is the secret; keep the grid wherever passwords are kept.
        /// </summary>
        /// <param name="grid">Grid to append the rows to</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowGetCredentials SaveOtpSecretsTo(IGPALGrid<string> grid)
        {
            List<OtpAccount> accounts = OtpHandler.ReadAccounts(OtpSecret);
            if (null == grid)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Grid cannot be null in SaveOtpSecretsTo.", null, GPALObjectType.None);
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{accounts.Count}] one-time password accounts to grid.", null, GPALObjectType.None);
                foreach (OtpAccount account in accounts)
                    grid.AddRow(new List<string> { account.Issuer, account.Name, account.Uri });
            }
            return this;
        }

        /// <summary>
        /// Sets the search term used to find a matching item in the target password manager (e.g. a site name or URL), and ensures an access token is available for ServiceType.
        /// </summary>
        /// <param name="target">Search term identifying the vault item to retrieve.</param>
        /// <returns>Fluent interface to continue building the credentials request, e.g. via .ToGPALObject() or .SaveTo(grid)</returns>
        public IAllowCredentialSettings GetCredentialsFor(string target)
        {
            Target = target;
            if (ServiceType != CredentialServiceType.None)
            {
                EnsureValidToken(out string accessToken);
            }
            return this;
        }

        /// <summary>
        /// Looks up the credential matching .GetCredentialsFor's target (filtered by .WithDomain if set) and appends a [Username, Password] row to the given grid.<br/>
        /// Typical usage: .WithSelector(usernameSelector).WithSelector(passwordSelector).FillInFrom(grid).LeftClick(loginSelector)
        /// </summary>
        /// <param name="grid">Grid to append the retrieved [Username, Password] row to.</param>
        /// <returns>Fluent interface to continue building the credentials request</returns>
        public IAllowGetCredentials SaveTo(IGPALGrid<string> grid)
        {
            if (grid == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Grid cannot be null in SaveTo.", null, GPALObjectType.None);
                return this;
            }

            var credentials = ServiceType switch
            {
                CredentialServiceType.Bitwarden => BitwardenHandler.Handle(this),
                CredentialServiceType.LastPass => LastPassHandler.Handle(this),
                CredentialServiceType.OnePassword => OnePasswordHandler.Handle(this),
                CredentialServiceType.Dashlane => DashlaneHandler.Handle(this),
                CredentialServiceType.Keeper => KeeperHandler.Handle(this),
                CredentialServiceType.None => DirectHandler.Handle(this),

                _ => throw new NotSupportedException(
                        $"SaveTo is not supported for CredentialServiceType.{ServiceType}.")
            };

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{credentials.Count()}] credentials from [{ServiceType}] to grid.", null, GPALObjectType.None);

            foreach (var credential in credentials)
            {
                grid.AddRow(new List<string> { credential.Username, credential.Password });
            }

            return this;
        }

        /// <summary>
        /// Fluent interface to return this credentials object as an ICredentials.
        /// </summary>
        /// <returns>This instance, as an ICredentials.</returns>
        public ICredentials ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Fetches a new access token for ServiceType using the credentials/configuration supplied so far, and stores it in AccessToken/IssueTime/ExpiresIn.
        /// </summary>
        /// <param name="accessToken">Receives the fetched access token, or an empty string if the fetch failed or ServiceType has no token fetcher.</param>
        public void FetchAccessToken(out string accessToken)
        {
            switch (ServiceType)
            {
                case CredentialServiceType.Google:
                    if (!string.IsNullOrEmpty(ServiceAccountKeyJson))
                    {
                        AccessToken = GoogleHandler.FetchServiceAccountToken(this);
                    }
                    else
                    {
                        AccessToken = GoogleHandler.FetchAccessToken(this); // Fallback to OAuth flow
                    }
                    break;
                case CredentialServiceType.AWS:
                    AccessToken = AWSHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.Azure:
                    AccessToken = AzureHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.Bitwarden:
                    AccessToken = BitwardenHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.Dashlane:
                    AccessToken = DashlaneHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.Keeper:
                    AccessToken = KeeperHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.LastPass:
                    AccessToken = LastPassHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.OnePassword:
                    AccessToken = OnePasswordHandler.FetchAccessToken(this);
                    break;
                case CredentialServiceType.StaticKey:
                    // The key supplied via .WithServiceKey/.WithKeyFromEnv/.WithKeyFromApi IS the credential,
                    // used directly as the Bearer/x-api-key header value - nothing to fetch.
                    AccessToken = ServiceKey;
                    break;
                default:
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No access token fetcher for service.", null, GPALObjectType.None);
                    break;
            }
            accessToken = AccessToken;
        }
        /// <summary>
        /// Returns true if AccessToken has not been fetched, or if it has passed ExpiresAt.
        /// </summary>
        /// <returns>True if a new access token must be fetched before use.</returns>
        private bool IsTokenExpired()
        {
            if (string.IsNullOrEmpty(AccessToken) || IssueTime == DateTime.MinValue) return true;
            return DateTime.UtcNow > ExpiresAt;
        }
    }

    /// <summary>
    /// A single username/password pair retrieved from a password manager vault or supplied directly, ready to be added as a row to a GPALGrid.
    /// </summary>
    internal class CredentialResult
    {
        /// <summary>
        /// Retrieved or supplied username/login.
        /// </summary>
        public string Username { get; }
        /// <summary>
        /// Retrieved or supplied password.
        /// </summary>
        public string Password { get; }

        public CredentialResult(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }

    /// <summary>
    /// Handles CredentialServiceType.None: Username/Password were set directly via .WithUsername/.WithPassword rather than retrieved from a password manager.
    /// </summary>
    internal static class DirectHandler
    {
        /// <summary>
        /// Returns the directly-supplied Username/Password as a single CredentialResult, or an empty list if both are empty.
        /// </summary>
        /// <param name="creds">Credentials with Username/Password set via .WithUsername/.WithPassword.</param>
        /// <returns>A single-item list containing the supplied username/password, or empty if both are unset.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            if (string.IsNullOrEmpty(creds.Username) && string.IsNullOrEmpty(creds.Password))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Username and Password are both empty.", null, GPALObjectType.None);
                return results;
            }

            results.Add(new CredentialResult(creds.Username ?? "", creds.Password ?? ""));
            return results;
        }
    }

    /// <summary>
    /// Fetches OAuth access tokens from an AWS Cognito user pool, via the refresh-token or authorization-code flow.
    /// </summary>
    internal static class AWSHandler
    {
        /// <summary>
        /// Fetches an AWS Cognito access token, refreshing via RefreshToken if available, otherwise running the authorization-code flow (which may launch a browser via AuthHandler).
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
                                $"refresh_token={Uri.EscapeDataString(creds.RefreshToken)}";

                var client = GPAL.RESTClient
                    .WithAPIBase(creds.Config.AWSAuthBase) // Use config property
                    .WithEndpoint(creds.Config.AWSAuthEndpoint)
                    .WithParameters(parameters);
                var response = client.Execute();

                try
                {
                    var authData = System.Text.Json.JsonSerializer.Deserialize<AWSTokenResponse>(response);

                    // Note: Cognito may not return a new refresh token
                    if (authData != null)
                    {
                        creds.AccessToken = authData.AccessToken ?? "";

                        if (string.IsNullOrEmpty(creds.AccessToken))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to refresh AWS access token. Response: [{response}]", null, GPALObjectType.None);
                        }
                        else if (!string.IsNullOrEmpty(authData?.RefreshToken))
                        {
                            creds.WithRefreshToken(authData.RefreshToken);
                        }

                        if (!string.IsNullOrEmpty(creds.AccessToken))
                        {
                            creds.IssueTime = DateTime.UtcNow;
                            creds.ExpiresIn = authData.ExpiresIn;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response);
                    string error = errorResponse?.ContainsKey("error") == true ? errorResponse["error"] : "Unknown error";
                    string errorDescription = errorResponse?.ContainsKey("error_description") == true ? errorResponse["error_description"] : "No description";
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to refresh AWS access token. Error: [{error}], Description: [{errorDescription}]", null, GPALObjectType.None, ex);
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
                                $"redirect_uri={Uri.EscapeDataString(creds.Config.AWSRedirectUri)}"; // Use AWS-specific redirect URI

                    var client = GPAL.RESTClient
                        .WithAPIBase(creds.Config.AWSAuthBase) // Use config property
                        .WithEndpoint(creds.Config.AWSAuthEndpoint)
                        .WithParameters(parameters);
                    var response = client.Execute();

                    try
                    {
                        var authData = System.Text.Json.JsonSerializer.Deserialize<AWSTokenResponse>(response);

                        if (authData != null)
                        {
                            creds.AccessToken = authData.AccessToken ?? "";
                            if (string.IsNullOrEmpty(creds.AccessToken))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fetch AWS access token. Response: [{response}]", null, GPALObjectType.None);
                            }
                            else if (!string.IsNullOrEmpty(authData?.RefreshToken))
                            {
                                creds.WithRefreshToken(authData.RefreshToken);
                            }

                            if (!string.IsNullOrEmpty(creds.AccessToken))
                            {
                                creds.IssueTime = DateTime.UtcNow;
                                creds.ExpiresIn = authData.ExpiresIn;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response);
                        string error = errorResponse?.ContainsKey("error") == true ? errorResponse["error"] : "Unknown error";
                        string errorDescription = errorResponse?.ContainsKey("error_description") == true ? errorResponse["error_description"] : "No description";
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fetch AWS access token. Error: [{error}], Description: [{errorDescription}]", null, GPALObjectType.None, ex);
                    }
                }
            }

            return creds.AccessToken;
        }
    }
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
                    .WithAPIBase(creds.Config.AzureAuthBase) // Use config property
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
                                $"redirect_uri={Uri.EscapeDataString(creds.Config.AzureRedirectUri)}&" + // Use Azure-specific redirect URI
                                $"scope={Uri.EscapeDataString(scopeUrl)}";

                    var client = GPAL.RESTClient
                        .WithAPIBase(creds.Config.AzureAuthBase) // Use config property
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

    /// <summary>
    /// One account a one-time password secret belongs to.
    /// </summary>
    internal class OtpAccount
    {
        /// <summary>
        /// The site or company, e.g. "Fidelity"; empty for a bare base32 key.
        /// </summary>
        public string Issuer { get; set; }
        /// <summary>
        /// The account at that site, normally the sign-in name or email; empty for a bare base32 key.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The account's otpauth://totp/ URI, or the bare base32 key it was given as.
        /// </summary>
        public string Uri { get; set; }
    }

    /// <summary>
    /// Makes RFC 6238 time-based one-time codes (TOTP), the 6 digits an authenticator app shows, from the secret a
    /// site hands out when two-factor sign-in is turned on.
    /// </summary>
    internal static class OtpHandler
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Makes the code for the given moment. The secret is either the bare base32 key or the whole otpauth://totp/
        /// URI, whose digits, period and algorithm then replace the defaults of 6, 30 seconds and SHA1.
        /// </summary>
        /// <param name="otpSecret">Base32 key, or an otpauth://totp/ URI holding it</param>
        /// <param name="utcNow">The moment to make the code for</param>
        /// <param name="validUntil">Receives the UTC time the code stops being current</param>
        /// <returns>The code, or an empty string if the secret could not be read</returns>
        internal static string GenerateCode(string otpSecret, DateTime utcNow, out DateTime validUntil)
        {
            string code = "";
            validUntil = utcNow;
            string secret = otpSecret?.Trim() ?? "";
            int digits = 6;
            int period = 30;
            string algorithm = "SHA1";
            string uriType = "totp";

            if (secret.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(secret, UriKind.Absolute, out Uri uri);
                uriType = uri?.Host ?? "";
                secret = "";
                foreach (string pair in (uri?.Query ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = pair.Split(new[] { '=' }, 2);
                    string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                    switch (parts[0].ToLowerInvariant())
                    {
                        case "secret": secret = value; break;
                        case "digits": int.TryParse(value, out digits); break;
                        case "period": int.TryParse(value, out period); break;
                        case "algorithm": algorithm = value.ToUpperInvariant(); break;
                    }
                }
            }

            byte[] key = Base32Decode(secret);
            if (false == string.Equals(uriType, "totp", StringComparison.OrdinalIgnoreCase))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Only otpauth://totp/ URIs make time-based codes; this one is [{uriType}].", null, GPALObjectType.Other);
            }
            else if (null == key || 0 == key.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "The one-time password secret is empty or is not base32.", null, GPALObjectType.Other);
            }
            else if (digits < 6 || digits > 8 || period < 1)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The one-time password URI asks for [{digits}] digits every [{period}] seconds; 6 to 8 digits and a period of 1 second or more are supported.", null, GPALObjectType.Other);
            }
            else
            {
                using (HMAC hmac = "SHA256" == algorithm ? (HMAC)new HMACSHA256(key) : "SHA512" == algorithm ? (HMAC)new HMACSHA512(key) : new HMACSHA1(key))
                {
                    long step = new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / period;
                    byte[] counter = BitConverter.GetBytes(step);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(counter);
                    byte[] hash = hmac.ComputeHash(counter);

                    // RFC 4226 dynamic truncation: the low 4 bits of the last byte pick where 4 bytes are read from
                    int offset = hash[hash.Length - 1] & 0x0F;
                    int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
                    code = (binary % (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
                    validUntil = DateTimeOffset.FromUnixTimeSeconds((step + 1) * period).UtcDateTime;
                }
            }
            return code;
        }

        /// <summary>
        /// Lists the accounts a secret holds. A bare base32 key or an otpauth://totp/ URI is one account; a Google
        /// Authenticator export (otpauth-migration://offline?data=...) packs every exported account into one QR code
        /// and becomes one otpauth://totp/ URI per account. Counter-based (HOTP) accounts in an export are skipped.
        /// </summary>
        /// <param name="otpSecret">Base32 key, otpauth://totp/ URI, or otpauth-migration:// URI</param>
        /// <returns>The accounts, empty when the secret is empty or the export cannot be read</returns>
        internal static List<OtpAccount> ReadAccounts(string otpSecret)
        {
            List<OtpAccount> accounts = new List<OtpAccount>();
            string secret = otpSecret?.Trim() ?? "";

            if (secret.StartsWith("otpauth-migration:", StringComparison.OrdinalIgnoreCase))
            {
                string data = "";
                int at = secret.IndexOf("data=", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                    data = Uri.UnescapeDataString(secret.Substring(at + 5).Split('&')[0]).Replace(' ', '+');
                try
                {
                    byte[] payload = Convert.FromBase64String(data);
                    int skipped = 0;
                    // MigrationPayload field 1 is each exported account (OtpParameters)
                    foreach (byte[] parameters in ProtobufFields(payload, 1))
                    {
                        byte[] key = ProtobufFields(parameters, 1).FirstOrDefault() ?? new byte[0];
                        string name = Encoding.UTF8.GetString(ProtobufFields(parameters, 2).FirstOrDefault() ?? new byte[0]);
                        string issuer = Encoding.UTF8.GetString(ProtobufFields(parameters, 3).FirstOrDefault() ?? new byte[0]);
                        long algorithm = ProtobufVarint(parameters, 4);
                        long digits = ProtobufVarint(parameters, 5);
                        long type = ProtobufVarint(parameters, 6);
                        if (1 == type)
                        {
                            skipped++;
                        }
                        else
                        {
                            string label = string.IsNullOrEmpty(issuer) || name.StartsWith(issuer + ":") ? name : $"{issuer}:{name}";
                            accounts.Add(new OtpAccount
                            {
                                Issuer = issuer,
                                Name = name,
                                Uri = $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={Base32Encode(key)}&issuer={Uri.EscapeDataString(issuer)}" +
                                      $"&algorithm={(2 == algorithm ? "SHA256" : 3 == algorithm ? "SHA512" : "SHA1")}&digits={(2 == digits ? 8 : 6)}&period=30"
                            });
                        }
                    }
                    if (skipped > 0)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The export holds [{skipped}] counter-based accounts, which make no time-based codes and were skipped.", null, GPALObjectType.Other);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "The otpauth-migration export could not be read", null, GPALObjectType.Other, ex);
                }
            }
            else if (secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(secret, UriKind.Absolute, out Uri uri);
                string label = Uri.UnescapeDataString(uri?.AbsolutePath.TrimStart('/') ?? "");
                string issuer = "";
                foreach (string pair in (uri?.Query ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (pair.StartsWith("issuer=", StringComparison.OrdinalIgnoreCase))
                        issuer = Uri.UnescapeDataString(pair.Substring(7));
                }
                int colon = label.IndexOf(':');
                if (string.IsNullOrEmpty(issuer) && colon > 0)
                    issuer = label.Substring(0, colon);
                accounts.Add(new OtpAccount { Issuer = issuer, Name = colon > 0 ? label.Substring(colon + 1) : label, Uri = secret });
            }
            else if (secret.Length > 0)
            {
                accounts.Add(new OtpAccount { Issuer = "", Name = "", Uri = secret });
            }
            return accounts;
        }

        /// <summary>
        /// Picks the account to make a code for: the only one there is, or, when an export holds several, the one
        /// whose issuer or account name contains the GetCredentialsFor target.
        /// </summary>
        /// <param name="accounts">The accounts ReadAccounts listed</param>
        /// <param name="target">Issuer or account name to look for, or null</param>
        /// <returns>The account, or null when none or more than one fits</returns>
        internal static OtpAccount ChooseAccount(List<OtpAccount> accounts, string target)
        {
            OtpAccount account = null;
            List<OtpAccount> matches = string.IsNullOrEmpty(target)
                ? accounts
                : accounts.Where(a => a.Issuer.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 || a.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (0 == accounts.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No one-time password secret was set; give it with WithOtpSecret or WithOtpQRCode.", null, GPALObjectType.Other);
            else if (1 == accounts.Count)
                account = accounts[0];
            else if (1 == matches.Count)
                account = matches[0];
            else if (0 == matches.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"None of the [{accounts.Count}] accounts in the export matches [{target}]; they are [{string.Join(", ", accounts.Select(a => a.Issuer))}].", null, GPALObjectType.Other);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{matches.Count}] of the accounts in the export match [{target}]; name one with GetCredentialsFor. They are [{string.Join(", ", matches.Select(a => $"{a.Issuer}:{a.Name}"))}].", null, GPALObjectType.Other);
            return account;
        }

        /// <summary>
        /// The length-delimited values of one field number in a protobuf message, which is how the export stores its
        /// accounts and their secrets, names and issuers.
        /// </summary>
        private static List<byte[]> ProtobufFields(byte[] message, int fieldNumber)
        {
            List<byte[]> values = new List<byte[]>();
            int position = 0;
            while (position < message.Length)
            {
                long tag = ReadVarint(message, ref position);
                int wireType = (int)(tag & 7);
                long length = 0 == wireType ? ReadVarint(message, ref position) : 2 == wireType ? ReadVarint(message, ref position) : 1 == wireType ? 8 : 4;
                if (2 == wireType && fieldNumber == (tag >> 3))
                {
                    byte[] value = new byte[length];
                    Array.Copy(message, position, value, 0, length);
                    values.Add(value);
                }
                position += 0 == wireType ? 0 : (int)length;
            }
            return values;
        }

        /// <summary>
        /// The value of one varint field in a protobuf message, or 0 when the field is absent.
        /// </summary>
        private static long ProtobufVarint(byte[] message, int fieldNumber)
        {
            long value = 0;
            int position = 0;
            while (position < message.Length)
            {
                long tag = ReadVarint(message, ref position);
                int wireType = (int)(tag & 7);
                if (0 == wireType)
                {
                    long read = ReadVarint(message, ref position);
                    if (fieldNumber == (tag >> 3))
                        value = read;
                }
                else
                {
                    // the length is read first, as it moves position past itself
                    int length = 2 == wireType ? (int)ReadVarint(message, ref position) : 1 == wireType ? 8 : 4;
                    position += length;
                }
            }
            return value;
        }

        /// <summary>
        /// Reads a protobuf base-128 varint and moves past it.
        /// </summary>
        private static long ReadVarint(byte[] message, ref int position)
        {
            long value = 0;
            int shift = 0;
            byte b = 0x80;
            while (0 != (b & 0x80) && position < message.Length)
            {
                b = message[position++];
                value |= (long)(b & 0x7F) << shift;
                shift += 7;
            }
            return value;
        }

        /// <summary>
        /// Encodes bytes as RFC 4648 base32 without padding, the form otpauth:// URIs carry the secret in.
        /// </summary>
        internal static string Base32Encode(byte[] data)
        {
            StringBuilder text = new StringBuilder();
            int buffer = 0;
            int bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    text.Append(Base32Alphabet[(buffer >> bits) & 31]);
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0)
                text.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
            return text.ToString();
        }

        /// <summary>
        /// Decodes an RFC 4648 base32 string. Case, spaces, hyphens and '=' padding are ignored, as sites show the key
        /// in groups like "jbsw y3dp ehpk 3pxp".
        /// </summary>
        /// <param name="value">The base32 text</param>
        /// <returns>The decoded bytes, or null if the text holds a character outside the base32 alphabet</returns>
        internal static byte[] Base32Decode(string value)
        {
            List<byte> bytes = new List<byte>();
            int buffer = 0;
            int bits = 0;
            bool valid = true;
            foreach (char c in (value ?? "").ToUpperInvariant())
            {
                int index = Base32Alphabet.IndexOf(c);
                if (index >= 0)
                {
                    buffer = (buffer << 5) | index;
                    bits += 5;
                    if (bits >= 8)
                    {
                        bits -= 8;
                        bytes.Add((byte)(buffer >> bits));
                        buffer &= (1 << bits) - 1;
                    }
                }
                else if (' ' != c && '-' != c && '=' != c)
                {
                    valid = false;
                }
            }
            return valid ? bytes.ToArray() : null;
        }
    }

    /// <summary>
    /// Minimal fallback HTTP listener for the OAuth redirect callback (e.g. http://localhost:3000/access-token).<br/>
    /// GPALRESTAPI normally serves this redirect and its paired get-access-token endpoint; if GPALRESTAPI is not
    /// running, StartIfNeeded binds the same base URL itself and serves the same two endpoints, so the OAuth
    /// redirect always has somewhere to land.
    /// </summary>
    internal static class LocalAuthCallbackServer
    {
        private static readonly object _lock = new object();
        private static HttpListener _listener;
        private static string _pendingAuthCode;
        private static string _pendingAuthScope;

        /// <summary>
        /// Starts listening on baseUrl if nothing is already listening there.
        /// </summary>
        /// <param name="baseUrl">The OAuth redirect base URL, e.g. GPAL.OAuthTokenUrl.</param>
        /// <returns>True if this call started the listener (the caller must call Stop() afterward); false if something else (e.g. GPALRESTAPI) already owns that prefix.</returns>
        internal static bool StartIfNeeded(string baseUrl)
        {
            lock (_lock)
            {
                if (null != _listener)
                    return true;

                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add(baseUrl);
                    listener.Start();
                    _listener = listener;

                    new Thread(() => Listen(listener)) { IsBackground = true }.Start();
                    return true;
                }
                catch (HttpListenerException)
                {
                    // Another listener owns this prefix. BrowserHelper.RestApiAnswering checks whether it is
                    // GPALRestAPI.
                    return false;
                }
            }
        }

        private static void Listen(HttpListener listener)
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context = listener.GetContext();
                    HandleRequest(context);
                }
            }
            catch (Exception)
            {
                // listener was stopped
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            string path = context.Request.Url.AbsolutePath.Trim('/');
            string json;

            switch (path)
            {
                case "access-token": // Called by the provider's redirect after the user grants consent
                    NameValueCollection query = ParseQueryString(context.Request.Url.Query);

                    lock (_lock)
                    {
                        _pendingAuthCode = query["code"];
                        _pendingAuthScope = query["scope"];
                    }

                    SendResponse(response, "{\"message\":\"Authorization code received. You can now call /get-access-token.\"}");
                    break;

                case "get-access-token": // Polled by GetAuthCodeFromRest
                    string code, scope;

                    lock (_lock)
                    {
                        code = _pendingAuthCode ?? string.Empty;
                        scope = _pendingAuthScope ?? string.Empty;

                        // single use
                        _pendingAuthCode = null;
                        _pendingAuthScope = null;
                    }

                    json = System.Text.Json.JsonSerializer.Serialize(new { code, scope });
                    SendResponse(response, json);
                    break;

                default:
                    SendResponse(response, "{}");
                    break;
            }
        }

        private static NameValueCollection ParseQueryString(string query)
        {
            var result = new NameValueCollection();

            if (string.IsNullOrEmpty(query))
                return result;

            foreach (string pair in query.TrimStart('?').Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                string[] parts = pair.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(parts[0]);
                string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }

        private static void SendResponse(HttpListenerResponse response, string message)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;

                byte[] buffer = Encoding.UTF8.GetBytes(message ?? "");
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch { /* client gone - ignore */ }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        /// <summary>
        /// Stops the listener started by StartIfNeeded, if this process owns it.
        /// </summary>
        internal static void Stop()
        {
            lock (_lock)
            {
                if (null == _listener)
                    return;

                try { _listener.Stop(); _listener.Close(); } catch { }
                _listener = null;
                _pendingAuthCode = null;
                _pendingAuthScope = null;
            }
        }
    }

    /// <summary>
    /// Drives the OAuth authorization-code flow (Google/Azure/AWS): builds provider authorization URLs and obtains the resulting authorization code, either via the GPAL REST redirect or manual browser/console entry.
    /// </summary>
    internal static class AuthHandler
    {
        // Default GPALRestAPI address, checked before starting our own listener.
        internal const string DefaultTokenUrl = "http://localhost:3000/";
        /// <summary>
        /// I believe this should get the auth token from the rest api page we just navigated to, but this requires OttoMagic because of the REST endpoint
        /// get-access-token is implemented in the restapi
        /// </summary>
        /// <param name="browserProcess"></param>
        /// <param name="timeoutInSeconds"></param>
        /// <returns></returns>
        internal static string GetAuthCodeFromRest(Process browserProcess, int timeoutInSeconds)
        {
            string authCode = "";

            try
            {
                // NOTE: get the access token from the rest API which is started by OttoMagic
                // GPALRestApi gets it because we pass a local url callback creds.Config.GoogleRestRedirectUri
                // and then we can access the token
                var restClient = GPAL.RESTClient
                    .WithAPIBase(GPAL.OAuthTokenUrl)
                    .WithEndpoint("get-access-token");

                // Polling parameters
                TimeSpan timeout = TimeSpan.FromSeconds(timeoutInSeconds);
                TimeSpan interval = TimeSpan.FromSeconds(1);
                DateTime start = DateTime.UtcNow;

                while ((DateTime.UtcNow - start) < timeout)
                {
                    AuthCodePayload tokenInfo = restClient.Execute<AuthCodePayload>(); // returns AuthCodePayload directly
                    if (!string.IsNullOrEmpty(tokenInfo?.Code))
                    {
                        authCode = tokenInfo.Code;
                        GPAL.PublishSimpleEvent(
                            GPALEventType.INFO,
                            "Authorization code retrieved from REST server.",
                            null,
                            GPALObjectType.None
                        );
                        break; // stop polling
                    }

                    Thread.Sleep(interval);
                }

                if (string.IsNullOrEmpty(authCode))
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Timeout waiting for authorization code from REST server.", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"REST redirect auth failed",
                    null,
                    GPALObjectType.None
                , ex);
            }
            if (null != browserProcess)
                BrowserHelper.KillProcess(browserProcess);  // only needed once to login

            return authCode;
        }

        /// <summary>
        /// Sets GPAL.OAuthTokenUrl to the host and port of GoogleRestRedirectUri, or DefaultTokenUrl. Starts a local
        /// listener there if the port is free; otherwise uses a GPALRestAPI already on it; otherwise publishes an
        /// error.
        /// </summary>
        /// <param name="creds">The credential being used.</param>
        /// <returns>True when this process started the listener and must stop it afterwards.</returns>
        internal static bool ResolveTokenUrl(Credentials creds)
        {
            bool retVal = false;
            string configured = creds?.Config?.GoogleRestRedirectUri;

            GPAL.OAuthTokenUrl = true == string.IsNullOrWhiteSpace(configured)
                ? DefaultTokenUrl
                : $"{new Uri(configured).GetLeftPart(UriPartial.Authority)}/";

            if (true == LocalAuthCallbackServer.StartIfNeeded(GPAL.OAuthTokenUrl))
            {
                retVal = true;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Listening on [{GPAL.OAuthTokenUrl}] to catch the token.", creds, GPALObjectType.Other);
            }
            else if (true == Browser.BrowserHelper.RestApiAnswering(GPAL.OAuthTokenUrl))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"The GPALRestAPI already on [{GPAL.OAuthTokenUrl}] will catch the token.", creds, GPALObjectType.Other);
            }
            else
            {
                // The provider only redirects to the registered port, so there is no fallback. Two of these on one
                // machine collide.
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Something that is not GPALRestAPI is on [{GPAL.OAuthTokenUrl}], and the redirect registered with the provider cannot point anywhere else", creds, GPALObjectType.Other);
            }

            return retVal;
        }

        // Returns the console window handle; zero when the program has no console (e.g. a forms program).
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        /// <summary>
        /// Get an authcode by calling the auth endpoint with a local 127.0.0.1:3000/get-access-token call
        /// </summary>
        /// <param name="creds">The credential to get an auth code for</param>
        /// <returns></returns>
        internal static string GetAuthCode(Credentials creds)
        {
            string authCode = "";
            int retry = 2;
            Process browserProcess = null;
            bool ownsLocalServer = false;

            bool publishToConsole = GPAL.GPALSettings.PublishToConsole;

            GPALEventType consoleEvents = GPAL.GPALSettings.ConsoleEvents;

            if (false == publishToConsole)
                GPAL.WithPublishToConsole();
            else if (false == consoleEvents.HasFlag(GPALEventType.INFO))
                GPAL.ConsoleEvents |= GPALEventType.INFO;

            // REST redirect flow
            // workflow : launch browser > user logs in (unknown length of time) > redirect to localhost > listener parses code
            //            meanwhile, GPAL is polling get-access-token until some value is returned or 1 minute passes
            if (creds.Config.UseRestRedirect)
            {
                // GPALRESTAPI normally serves the redirect target (e.g. http://localhost:3000/access-token) and
                // get-access-token. If it isn't running, start a minimal listener on the same base URL ourselves
                // so the OAuth redirect still has somewhere to land.
                ownsLocalServer = ResolveTokenUrl(creds);

                string authUrl = AuthHandler.GetAuthUrl(creds);

                browserProcess = creds.Browser.MagicHelper.LaunchBrowser(creds.Browser, authUrl);   // launch a new instance of the browser for authentication
                authCode = GetAuthCodeFromRest(browserProcess, 60);
            }

            // Manual fallback flow: either UseRestRedirect is false, or the REST redirect timed out
            if (string.IsNullOrEmpty(authCode))
            {
                string authUrl = AuthHandler.GetAuthUrl(creds);

                if (true == creds.Config.UseRestRedirect)
                    GPAL.PublishSimpleEvent(
                        GPALEventType.WARNING,
                        "No authorization code arrived via the local redirect listener. Falling back to manual entry.",
                        null,
                        GPALObjectType.None
                    );

                // The auth code must be typed in. A forms program has no console, and reading from one hangs forever,
                // so publish an error; a forms workflow passes the code with WithAuthCode.
                if (IntPtr.Zero == GetConsoleWindow())
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"An authorization code is needed and this program has no console to type it into. Open this URL, authorize, and hand the code back with WithAuthCode, or supply a refresh token with WithRefreshToken: [{authUrl}]");
                }
                else
                {
                    while (retry > 0)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.INFO,
                            $"Open this URL in a browser, authorize, and provide the code: \n[{authUrl}]",
                            null,
                            GPALObjectType.None
                        );

                        Console.WriteLine("\nEnter auth code: ");   // a prompt for the ReadLine below, not an event
                        authCode = Console.ReadLine();

                        if (string.IsNullOrEmpty(authCode))
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                "No authorization code provided.",
                                null,
                                GPALObjectType.None
                            );
                            retry--;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            if (ownsLocalServer)
                LocalAuthCallbackServer.Stop();

            if (false == publishToConsole)
                GPAL.WithPublishToConsole(GPALEventType.NONE);
            else if (false == consoleEvents.HasFlag(GPALEventType.INFO))
                GPAL.ConsoleEvents ^= GPALEventType.INFO;

            return authCode;
        }

        /// <summary>
        /// Builds the provider-specific OAuth authorization URL (Google, Azure, or AWS) using the credentials' ClientId, configured redirect URI, and OAuthScope.
        /// </summary>
        /// <param name="creds">Credentials with ServiceType, ClientId, OAuthScope, and Config set.</param>
        /// <returns>The authorization URL to open in a browser, or an empty string if the service type does not support OAuth (e.g. Bitwarden).</returns>
        internal static string GetAuthUrl(Credentials creds)
        {
            switch (creds.ServiceType)
            {
                case CredentialServiceType.Google:
                    return $"{creds.Config.GoogleConsentBase}/o/oauth2/v2/auth" +
                           $"?client_id={Uri.EscapeDataString(creds.ClientId)}" +
                           $"&redirect_uri={Uri.EscapeDataString(creds.Config.UseRestRedirect ? creds.Config.GoogleRestRedirectUri : creds.Config.GoogleRedirectUri)}" +
                           $"&response_type=code" +
                           $"&scope={Uri.EscapeDataString(Credentials.GetScopeValue(creds.OAuthScope))}" +
                           $"&access_type=offline";

                case CredentialServiceType.Azure:
                    return $"{creds.Config.AzureAuthBase}/oauth2/v2.0/authorize" +
                           $"?client_id={Uri.EscapeDataString(creds.ClientId)}" +
                           $"&response_type=code" +
                           $"&redirect_uri={Uri.EscapeDataString(creds.Config.AzureRedirectUri)}" +
                           $"&response_mode=query" +
                           $"&scope={Uri.EscapeDataString(Credentials.GetScopeValue(creds.OAuthScope))}";

                case CredentialServiceType.AWS:
                    return $"{creds.Config.AWSAuthBase}/oauth2/authorize" +
                           $"?client_id={Uri.EscapeDataString(creds.ClientId)}" +
                           $"&response_type=code" +
                           $"&redirect_uri={Uri.EscapeDataString(creds.Config.AWSRedirectUri)}" +
                           $"&scope={Uri.EscapeDataString(Credentials.GetScopeValue(creds.OAuthScope))}";

                case CredentialServiceType.Bitwarden:
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Bitwarden OAuth not implemented.", creds, GPALObjectType.Other);
                    break;
            }

            return "";
        }
    }
}
