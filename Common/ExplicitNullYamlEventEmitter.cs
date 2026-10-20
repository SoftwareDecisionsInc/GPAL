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

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

public class ExplicitNullYamlEventEmitter : ChainedEventEmitter
{
    public ExplicitNullYamlEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Value == null)
        {
            // Emit quoted "null" string instead of blank or ~
            // emitter.Emit(new Scalar(null, null, "null", ScalarStyle.DoubleQuoted, true, false));
            emitter.Emit(new Scalar("null"));
            return;
        }

        base.Emit(eventInfo, emitter);
    }
}
