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
}
