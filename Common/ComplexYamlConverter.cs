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
        object complex = ConverterHelper.ParseComplex(scalar.ToString());
        // text that is not a complex number is not read here, so the reader that reads it as text says so
        if (null == complex)
            throw new FormatException($"[{scalar.ToString()}] is not a complex number");
        return complex;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        var complexNumber = (Complex)value;
        // the imaginary part keeps its sign, a negative zero included
        string separator = 0 > BitConverter.DoubleToInt64Bits(complexNumber.Imaginary) ? "-" : "+";
        emitter.Emit(new Scalar(null, null, $"{ConverterHelper.NumberText(complexNumber.Real)}{separator}{ConverterHelper.NumberText(Math.Abs(complexNumber.Imaginary))}i"));
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        object complex = ConverterHelper.ParseComplex(scalar.Value);
        // text that is not a complex number is not read here, so the reader that reads it as text says so
        if (null == complex)
            throw new FormatException($"[{scalar.Value}] is not a complex number");
        return complex;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        var complexNumber = (Complex)value;

        // the imaginary part keeps its sign, a negative zero included
        string separator = 0 > BitConverter.DoubleToInt64Bits(complexNumber.Imaginary) ? "-" : "+";
        emitter.Emit(new Scalar(null, null, $"{ConverterHelper.NumberText(complexNumber.Real)}{separator}{ConverterHelper.NumberText(Math.Abs(complexNumber.Imaginary))}i"));
    }

}

// Writes a double or a float as the other formats write it, the shortest text that reads back as the same number, on
// .NET Framework too, -0 for a negative zero, and .nan, .inf and -.inf as YAML writes them; read back the same way, -0
// keeping its sign, which .NET Framework drops
public class NumberYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        bool retVal = typeof(double) == type || typeof(float) == type || typeof(double?) == type || typeof(float?) == type;
        return retVal;
    }

    public object ReadYaml(IParser parser, Type type)
    {
        string text = parser.Consume<Scalar>().Value;
        // a member written as null, ~ or nothing is a null, or 0 for a member that cannot hold a null, as YAML reads it
        bool noNumber = "null" == text || "~" == text || 0 == text.Length;
        bool isNull = null != Nullable.GetUnderlyingType(type) && true == noNumber;
        double number = true == noNumber ? 0
            : ".nan" == text.ToLowerInvariant() ? double.NaN
            : ".inf" == text.ToLowerInvariant() || "+.inf" == text.ToLowerInvariant() ? double.PositiveInfinity
            : "-.inf" == text.ToLowerInvariant() ? double.NegativeInfinity
            : double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        // a zero written with a minus is a negative zero, which .NET Framework reads as 0
        if (0 == number && true == text.StartsWith("-", StringComparison.Ordinal))
            number = -0.0;
        object retVal = true == isNull ? null : typeof(float) == type || typeof(float?) == type ? (object)(float)number : number;
        return retVal;
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        object retVal = ReadYaml(parser, type);
        return retVal;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        // a nullable member that is null is written as null, as the other converters write it
        double number = null == value ? 0 : value is float single ? single : (double)value;
        string text = null == value ? "null"
            : true == double.IsNaN(number) ? ".nan"
            : true == double.IsPositiveInfinity(number) ? ".inf"
            : true == double.IsNegativeInfinity(number) ? "-.inf"
            : value is float singleText ? ConverterHelper.NumberText(singleText) : ConverterHelper.NumberText(number);
        emitter.Emit(new Scalar(null, null, text));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        WriteYaml(emitter, value, type);
    }
}
