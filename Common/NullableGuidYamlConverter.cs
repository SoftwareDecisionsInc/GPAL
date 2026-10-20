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
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Writes a Guid, and a null Guid?, the way the rest of GPAL writes nulls. Without this YamlDotNet has no
/// handling for the type and renders a null as an empty string, which does not read back as a null.
/// </summary>
public class NullableGuidYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Guid) || typeof(Guid?) == type;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();

        if (string.IsNullOrEmpty(scalar.Value) || scalar.Value.ToLower() == "null")
            return null;

        return Guid.Parse(scalar.Value);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalarValue = rootDeserializer(typeof(string)) as string;

        if (string.IsNullOrEmpty(scalarValue) || scalarValue.ToLower() == "null")
            return null;

        return Guid.Parse(scalarValue);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            emitter.Emit(new Scalar(null, null, ((Guid)value).ToString()));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            serializer(((Guid)value).ToString(), typeof(string));
    }
}
