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
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Writes a TimeSpan, and a null TimeSpan?, the way the rest of GPAL writes nulls. Without this YamlDotNet has no
/// handling for the type and renders a null as an empty string, which does not read back as a null.<br/>
/// The "c" format is the round-trippable one, matching what ConverterHelper writes elsewhere.
/// </summary>
public class NullableTimeSpanYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(TimeSpan) || typeof(TimeSpan?) == type;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();

        if (string.IsNullOrEmpty(scalar.Value) || scalar.Value.ToLower() == "null")
            return null;

        return TimeSpan.Parse(scalar.Value, CultureInfo.InvariantCulture);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalarValue = rootDeserializer(typeof(string)) as string;

        if (string.IsNullOrEmpty(scalarValue) || scalarValue.ToLower() == "null")
            return null;

        return TimeSpan.Parse(scalarValue, CultureInfo.InvariantCulture);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            emitter.Emit(new Scalar(null, null, ((TimeSpan)value).ToString("c")));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            serializer(((TimeSpan)value).ToString("c"), typeof(string));
    }
}
