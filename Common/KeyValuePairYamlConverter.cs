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
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Reads and writes a KeyValuePair as a mapping of its key and its value. A KeyValuePair's Key and Value cannot be set
/// once it is made, so YamlDotNet on its own makes an empty pair.
/// </summary>
public class KeyValuePairYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type.IsGenericType && typeof(KeyValuePair<,>) == type.GetGenericTypeDefinition();
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        Type[] pairTypes = type.GetGenericArguments();
        object key = null;
        object value = null;
        object pair;

        // a null pair (~, null or nothing) reads as an empty pair
        if (true == parser.Accept<Scalar>(out Scalar scalar) && true == IsNull(scalar))
        {
            parser.MoveNext();
            pair = Activator.CreateInstance(type);
        }
        else
        {
            parser.Consume<MappingStart>();
            while (false == parser.TryConsume<MappingEnd>(out _))
            {
                string name = parser.Consume<Scalar>().Value;

                // key and value, whatever their case; anything else is skipped
                if (true == "key".Equals(name, StringComparison.OrdinalIgnoreCase))
                    key = rootDeserializer(pairTypes[0]);
                else if (true == "value".Equals(name, StringComparison.OrdinalIgnoreCase))
                    value = rootDeserializer(pairTypes[1]);
                else
                    parser.SkipThisAndNestedEvents();
            }

            pair = Activator.CreateInstance(type, key, value);
        }

        return pair;
    }

    /// <summary>
    /// True for an unquoted ~, null, Null, NULL or empty scalar, which YAML reads as null.
    /// </summary>
    private static bool IsNull(Scalar scalar)
    {
        return ScalarStyle.Plain == scalar.Style
            && (0 == scalar.Value.Length || "~" == scalar.Value || "null" == scalar.Value || "Null" == scalar.Value || "NULL" == scalar.Value);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        Type[] pairTypes = type.GetGenericArguments();

        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar("Key"));
        serializer(type.GetProperty("Key").GetValue(value), pairTypes[0]);
        emitter.Emit(new Scalar("Value"));
        serializer(type.GetProperty("Value").GetValue(value), pairTypes[1]);
        emitter.Emit(new MappingEnd());
    }
}
