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
using System.Net;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

public class IPAddressYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(IPAddress);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        if (IPAddress.TryParse(value, out var ipAddress))
        {
            return ipAddress;
        }
        return null; // Handle parsing failure accordingly
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value is IPAddress ipAddress)
        {
            emitter.Emit(new Scalar(ipAddress.ToString()));
        }
        else
        {
            emitter.Emit(new Scalar(null)); // Handle writing failure accordingly
        }
    }
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        IPAddress retVal = null;
        string value;

        // Read the parser events directly. Asking rootDeserializer for an IPAddress would recurse into this converter
        // until an uncatchable stack overflow. Accept a one-key mapping (WriteYaml below) and a plain scalar (the other
        // overload).
        if (true == parser.Accept<MappingStart>(out _))
        {
            parser.Consume<MappingStart>();
            parser.Consume<Scalar>();                       // the "ipAddress" key
            value = parser.Consume<Scalar>().Value;
            parser.Consume<MappingEnd>();
        }
        else
            value = parser.Consume<Scalar>().Value;

        // A written null reads back as the scalar "null"; return null.
        if (false == string.IsNullOrWhiteSpace(value) && false == "null".Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            if (false == IPAddress.TryParse(value.Trim(), out retVal))
                throw new YamlException($"Invalid IP address format: {value}");

        return retVal;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value is IPAddress ipAddress)
        {
            // Emit the mapping for IP address in YAML format
            emitter.Emit(new MappingStart()); // Start a new mapping

            emitter.Emit(new Scalar("ipAddress")); // Emit the field name
            serializer(ipAddress.ToString()); // Use the serializer to handle the value

            emitter.Emit(new MappingEnd()); // End the mapping
        }
        else if (value == null)
        {
            // Write null instead of throwing; one null entry stopped the whole conversion. The other overload already
            // does this.
            emitter.Emit(new Scalar("null"));
        }
        else
        {
            throw new ArgumentException("Expected an IPAddress object.", nameof(value));
        }
    }
}

