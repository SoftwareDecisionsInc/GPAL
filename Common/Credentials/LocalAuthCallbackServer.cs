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
}
