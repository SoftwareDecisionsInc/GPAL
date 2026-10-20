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
/// Writes a DateTimeOffset as one "o" format string (keeps the offset), and null as null. Otherwise YamlDotNet writes
/// every property of the struct, which cannot be read back.
/// </summary>
public class DateTimeOffsetYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(DateTimeOffset) || typeof(DateTimeOffset?) == type;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();

        if (string.IsNullOrEmpty(scalar.Value) || scalar.Value.ToLower() == "null")
            return null;

        return DateTimeOffset.Parse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalarValue = rootDeserializer(typeof(string)) as string;

        if (string.IsNullOrEmpty(scalarValue) || scalarValue.ToLower() == "null")
            return null;

        return DateTimeOffset.Parse(scalarValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            emitter.Emit(new Scalar(null, null, ((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture)));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            serializer(((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture), typeof(string));
    }
}
