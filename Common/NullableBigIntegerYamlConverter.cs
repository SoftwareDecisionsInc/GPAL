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
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

public class NullableBigIntegerYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(BigInteger) || typeof(BigInteger?) == type;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();
        if (string.IsNullOrEmpty(scalar.Value) || scalar.Value.ToLower() == "null")
        {
            return null; // Handle null or empty string as null
        }
        return BigInteger.Parse(scalar.Value);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Deserialize the scalar value (which should be a string representation of the number)
        var scalarValue = rootDeserializer(typeof(string)) as string;

        if (string.IsNullOrEmpty(scalarValue) || scalarValue.ToLower() == "null")
        {
            return null; // Handle null or empty string as null
        }

        // Parse the BigInteger from the string
        return BigInteger.Parse(scalarValue);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            emitter.Emit(new Scalar(null, null, ((BigInteger)value).ToString()));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
            emitter.Emit(new Scalar("null"));
        else
            serializer(((BigInteger)value).ToString(), typeof(string));
    }

}

