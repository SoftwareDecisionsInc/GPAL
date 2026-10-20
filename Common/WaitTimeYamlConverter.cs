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
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;
using static GenerallyPositive.Enums;

public sealed class WaitTimeYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(WaitTime);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Handle scalar form: "Forever", "500", "Immediate", etc.
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext(); // consume the scalar

            if (WaitTime.TryFromString(scalar.Value, out var result))
            {
                return result;
            }

            throw new YamlException(scalar.Start, scalar.End,
                $"Unable to parse WaitTime from scalar value '{scalar.Value}'.");
        }

        // Handle mapping form: { Forever: null }, { 1000: null }, {}, etc.
        if (parser.Current is MappingStart)
        {
            parser.MoveNext(); // consume MappingStart

            // Deserialize the mapping as a dictionary
            var dictionary = (IDictionary<object, object>)rootDeserializer(typeof(Dictionary<object, object>));

            // Expect and consume MappingEnd
            if (!(parser.Current is MappingEnd))
            {
                throw new YamlException("Expected MappingEnd after WaitTime mapping.");
            }
            parser.MoveNext(); // consume MappingEnd

            if (WaitTime.TryFromDictionary(dictionary, out var result))
            {
                return result;
            }

            throw new YamlException("Invalid mapping format for WaitTime.");
        }

        // Fallback (null or unexpected) > Immediate
        parser.MoveNext();
        return WaitTime.Immediate;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        var waitTime = value is WaitTime wt ? wt : WaitTime.Immediate;

        // Emit clean scalar: Forever, Never, Immediate, or the number
        string text = waitTime.ToString();

        emitter.Emit(new Scalar(text));
    }
}
