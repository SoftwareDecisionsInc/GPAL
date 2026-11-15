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
}
