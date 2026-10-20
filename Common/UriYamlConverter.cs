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

public class UriYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Uri);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();
        return new Uri(scalar.Value); // Parse the scalar string as a Uri
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Deserialize the scalar value as a string using rootDeserializer
        var scalarValue = rootDeserializer(typeof(string)) as string;

        if (string.IsNullOrEmpty(scalarValue))
        {
            throw new YamlException("Invalid Uri format: string is null or empty.");
        }

        return new Uri(scalarValue); // Parse the string as a Uri
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value == null)
        {
            // Return null for a null value; one null Uri stopped the whole conversion.
            emitter.Emit(new Scalar("null"));
        }
        else
        {
            var uri = (Uri)value;
            emitter.Emit(new Scalar(null, null, uri.ToString())); // Emit the Uri as a string
        }
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
        {
            emitter.Emit(new Scalar("null")); // a null-valued scalar renders as '' which does not read back as null
        }
        else
        {
            var uri = (Uri)value;
            // Use the serializer to emit the Uri as a string
            serializer(uri.ToString(), typeof(string));
        }
    }

}

