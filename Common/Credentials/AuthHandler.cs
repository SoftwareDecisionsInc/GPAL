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
                // get the access token from the rest API which is started by OttoMagic
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
            // the sign-in browser is needed once, to sign in; its children go with it
            if (null != browserProcess)
                BrowserHelper.KillProcessTree(browserProcess.Id);

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

                // The sign-in page opens in a browser made only for it, which is not one of GPAL.Browsers
                Browser.Browser signInBrowser = new Browser.Browser { Name = "OAuth sign-in" };
                signInBrowser.BrowserSettings.MagicHelper = new Browser.MagicHelper(signInBrowser);
                try
                {
                    browserProcess = signInBrowser.MagicHelper.LaunchBrowser(signInBrowser, authUrl);
                    authCode = GetAuthCodeFromRest(browserProcess, 60);
                }
                finally
                {
                    // A launch that threw after starting the browser leaves no browserProcess to kill, so its tree is
                    // ended here
                    if (null == browserProcess && null != signInBrowser.BrowserSettings.Process)
                        BrowserHelper.KillProcessTree(signInBrowser.BrowserSettings.Process.Id);

                    // Not one of GPAL.Browsers, so the exit cleanup never sees it: its temp profile is removed here
                    if (true == signInBrowser.BrowserSettings.TempProfileCreated)
                        Browser.ChromeProfileManager.RemoveTempUserProfile(signInBrowser.BrowserSettings.ProfileDataDirectory);
                }
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
