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
    /// Deserialized contents of a Google Cloud service account JSON key file, used by GoogleHandler.FetchServiceAccountToken to sign a JWT.
    /// Each property carries the snake_case name Google writes in the key file.
    /// </summary>
    public class GoogleServiceAccountKey
    {
        /// <summary>
        /// Key type, normally "service_account".
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }
        /// <summary>
        /// Google Cloud project ID that owns this service account.
        /// </summary>
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; }
        /// <summary>
        /// Identifier of the private key within the service account.
        /// </summary>
        [JsonPropertyName("private_key_id")]
        public string PrivateKeyId { get; set; }
        /// <summary>
        /// PEM-encoded RSA private key used to sign the JWT assertion.
        /// </summary>
        [JsonPropertyName("private_key")]
        public string PrivateKey { get; set; }
        /// <summary>
        /// Email address of the service account; used as the JWT issuer (iss claim).
        /// </summary>
        [JsonPropertyName("client_email")]
        public string ClientEmail { get; set; }
        /// <summary>
        /// OAuth client ID associated with the service account.
        /// </summary>
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
        /// <summary>
        /// Google OAuth authorization endpoint URL.
        /// </summary>
        [JsonPropertyName("auth_uri")]
        public string AuthUri { get; set; }
        /// <summary>
        /// Google OAuth token endpoint URL used to exchange the signed JWT for an access token.
        /// </summary>
        [JsonPropertyName("token_uri")]
        public string TokenUri { get; set; }
        /// <summary>
        /// URL of Google's X.509 certificate provider for verifying tokens.
        /// </summary>
        [JsonPropertyName("auth_provider_x509_cert_url")]
        public string AuthProviderX509CertUrl { get; set; }
        /// <summary>
        /// URL of this service account's X.509 certificate.
        /// </summary>
        [JsonPropertyName("client_x509_cert_url")]
        public string ClientX509CertUrl { get; set; }

        public GoogleServiceAccountKey() { }
    }
}
