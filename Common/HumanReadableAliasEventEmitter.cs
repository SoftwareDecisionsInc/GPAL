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

using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

public class HumanReadableAliasEventEmitter : ChainedEventEmitter
{
    private readonly Dictionary<string, string> _anchorRenames = new Dictionary<string, string>();
    private readonly Dictionary<string, int> _typeCounters = new Dictionary<string, int>();

    public HumanReadableAliasEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }

    public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
    {
        if (!eventInfo.Anchor.IsEmpty)
            eventInfo.Anchor = new AnchorName(Rename(eventInfo.Anchor.Value, eventInfo.Source.Type?.Name));
        base.Emit(eventInfo, emitter);
    }

    public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
    {
        if (!eventInfo.Anchor.IsEmpty)
            eventInfo.Anchor = new AnchorName(Rename(eventInfo.Anchor.Value, eventInfo.Source.Type?.Name));
        base.Emit(eventInfo, emitter);
    }

    public override void Emit(AliasEventInfo eventInfo, IEmitter emitter)
    {
        if (_anchorRenames.TryGetValue(eventInfo.Alias.Value, out var readable))
            emitter.Emit(new AnchorAlias(new AnchorName(readable)));
        else
            base.Emit(eventInfo, emitter);
    }

    private string Rename(string original, string typeName)
    {
        var clean = CleanTypeName(typeName);
        _typeCounters.TryGetValue(clean, out int count);
        _typeCounters[clean] = count + 1;
        var readable = count == 0 ? clean : $"{clean}_{count + 1}";
        _anchorRenames[original] = readable;
        return readable;
    }

    // The type name without its generic arity and without the characters an anchor cannot hold, so an Address[] is
    // Address and an int[,] is int
    private static string CleanTypeName(string name)
    {
        if (name == null) return "object";
        var backtick = name.IndexOf('`');
        var clean = backtick >= 0 ? name.Substring(0, backtick) : name;
        clean = new string(clean.Where(c => false == "[]{},".Contains(c) && false == char.IsWhiteSpace(c)).ToArray());
        return 0 == clean.Length ? "object" : clean;
    }
}
