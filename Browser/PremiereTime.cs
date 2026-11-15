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
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using static GenerallyPositive.Enums;
using GenerallyPositive;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Represents a premiere schedule time. Presets (In5Minutes, In1Hour, etc.) store a relative
    /// offset and resolve to UtcNow + offset at the moment Resolve() is called, so the premiere
    /// time is set relative to when each video finishes processing, not when the chain was built.
    /// </summary>
    public readonly struct PremiereTime
    {
        private readonly bool     _isRelative;
        private readonly DateTime _absolute;
        private readonly TimeSpan _offset;

        private PremiereTime(TimeSpan offset)   { _isRelative = true;  _offset   = offset;                                                        _absolute = default; }
        private PremiereTime(DateTime absolute) { _isRelative = false; _absolute = absolute.Kind == DateTimeKind.Utc ? absolute : absolute.ToUniversalTime(); _offset = default; }

        public static PremiereTime In1Minute   => new PremiereTime(TimeSpan.FromMinutes(1));
        public static PremiereTime In5Minutes  => new PremiereTime(TimeSpan.FromMinutes(5));
        public static PremiereTime In10Minutes => new PremiereTime(TimeSpan.FromMinutes(10));
        public static PremiereTime In15Minutes => new PremiereTime(TimeSpan.FromMinutes(15));
        public static PremiereTime In30Minutes => new PremiereTime(TimeSpan.FromMinutes(30));
        public static PremiereTime In45Minutes => new PremiereTime(TimeSpan.FromMinutes(45));
        public static PremiereTime In1Hour     => new PremiereTime(TimeSpan.FromHours(1));

        public static PremiereTime At(DateTime dt) => new PremiereTime(dt);

        internal DateTime Resolve() => _isRelative ? DateTime.UtcNow.Add(_offset) : _absolute;

        public static implicit operator DateTime(PremiereTime pt) => pt.Resolve();

        public override string ToString() => _isRelative
            ? $"In {(int)_offset.TotalMinutes} minutes"
            : _absolute.ToString("o");
    }
}
