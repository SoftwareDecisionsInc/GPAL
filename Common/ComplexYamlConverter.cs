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
using GenerallyPositive;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

public class ComplexYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        bool retVal = type == typeof(Complex);
        return retVal;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();
        return ConverterHelper.ParseComplex(scalar.ToString());
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        var complexNumber = (Complex)value;
        string separator = (complexNumber.Imaginary >= 0 ? "+" : "-");
        emitter.Emit(new Scalar(null, null, $@"{complexNumber.Real}{separator}{Math.Abs(complexNumber.Imaginary)}i"));
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        return ConverterHelper.ParseComplex(scalar.Value);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        var complexNumber = (Complex)value;

        string separator = (complexNumber.Imaginary >= 0 ? "+" : "-");
        emitter.Emit(new Scalar(null, null, $"{complexNumber.Real}{separator}{Math.Abs(complexNumber.Imaginary)}i"));

        // this saves as 
        // real: 123
        // imaginary: 123i
        // we want real+imaginary, easier to parse :)
        //emitter.Emit(new MappingStart());

        //emitter.Emit(new Scalar(null, "real"));
        //serializer(complexNumber.Real);

        //emitter.Emit(new Scalar(null, "imaginary"));
        //serializer(complexNumber.Imaginary);

        //emitter.Emit(new MappingEnd());
    }

}

