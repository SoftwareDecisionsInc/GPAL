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
}
