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
            // Write null for a null value, so one null Uri does not stop the conversion.
            emitter.Emit(new Scalar("null"));
        }
        else
        {
            var uri = (Uri)value;
            emitter.Emit(new Scalar(null, null, uri.OriginalString)); // the text it was made from, as every format writes it
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
            serializer(uri.OriginalString, typeof(string));
        }
    }

}

// A class that has a converter to and from text, such as a Font or a CultureInfo, or a struct of the framework that has
// one, such as a System.Drawing.Point, is written as one value, its converter's text, "Arial, 9.75pt, style=Bold",
// "de-DE" or "3, 4", and read back through that converter
internal class TextConverterYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return true == GenerallyPositive.ConverterHelper.IsTextClassType(type) || true == GenerallyPositive.ConverterHelper.IsTextStructType(type);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        string text = parser.Consume<Scalar>().Value;
        return "null" == text || "~" == text || "" == text ? null : System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text);   // a null is written null
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        string text = parser.Consume<Scalar>().Value;
        return "null" == text || "~" == text || "" == text ? null : System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertFromInvariantString(text);   // a null is written null
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        emitter.Emit(null == value ? new Scalar("null") : new Scalar(null, null, System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertToInvariantString(value)));
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (null == value)
            emitter.Emit(new Scalar("null"));   // a null-valued scalar renders as '' which does not read back as null
        else
            serializer(System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertToInvariantString(value), typeof(string));
    }
}

