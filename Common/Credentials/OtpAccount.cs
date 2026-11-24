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
}
