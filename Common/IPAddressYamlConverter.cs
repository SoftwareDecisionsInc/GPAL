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
        return null; // the text is not an IP address
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        if (value is IPAddress ipAddress)
        {
            emitter.Emit(new Scalar(ipAddress.ToString()));
        }
        else
        {
            emitter.Emit(new Scalar(null)); // not an IP address, written as null
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
            // the address as its text, 192.168.0.1, as every format writes it; the reader above still takes the
            // ipAddress: mapping earlier versions wrote
            emitter.Emit(new Scalar(ipAddress.ToString()));
        }
        else if (value == null)
        {
            // Write null instead of throwing, so one null entry does not stop the conversion, as the other overload
            // does.
            emitter.Emit(new Scalar("null"));
        }
        else
        {
            throw new ArgumentException("Expected an IPAddress object.", nameof(value));
        }
    }
}

