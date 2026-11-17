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

        internal Credentials()
        {
            Config = CredentialsConfig.Load();
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
        [Obsolete("GoogleServiceAccountKey becomes internal in 2.0; call WithServiceAccountKey with the key file or its JSON instead.")]
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
}
