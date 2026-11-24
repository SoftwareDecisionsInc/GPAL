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
}
