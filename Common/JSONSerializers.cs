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
using System.Net;
using System.Numerics;
using Newtonsoft.Json;

namespace GenerallyPositive
{
    public class IPAddressJsonConverter : JsonConverter<IPAddress>
    {
        public override void WriteJson(JsonWriter writer, IPAddress value, JsonSerializer serializer)
        {
            writer.WriteValue(value?.ToString());
        }

        public override IPAddress ReadJson(JsonReader reader, Type objectType, IPAddress existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value as string;
            return string.IsNullOrEmpty(s) ? null : IPAddress.Parse(s);
        }
    }

    public class ComplexJsonConverter : JsonConverter<Complex>
    {
        public override void WriteJson(JsonWriter writer, Complex value, JsonSerializer serializer)
        {
            // the imaginary part keeps its sign, a negative zero included
            string separator = 0 > BitConverter.DoubleToInt64Bits(value.Imaginary) ? "-" : "+";
            writer.WriteValue($"{ConverterHelper.NumberText(value.Real)}{separator}{ConverterHelper.NumberText(Math.Abs(value.Imaginary))}i");
        }

        public override Complex ReadJson(JsonReader reader, Type objectType, Complex existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value as string;
            if (string.IsNullOrEmpty(s)) return Complex.Zero;

            return ConverterHelper.ParseComplex(s);
        }
    }

    public class UriJsonConverter : JsonConverter<Uri>
    {
        public override void WriteJson(JsonWriter writer, Uri value, JsonSerializer serializer)
        {
            writer.WriteValue(value?.OriginalString);
        }

        public override Uri ReadJson(JsonReader reader, Type objectType, Uri existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value as string;
            return string.IsNullOrEmpty(s) ? null : new Uri(s, UriKind.RelativeOrAbsolute);
        }
    }

    public class VersionJsonConverter : JsonConverter<Version>
    {
        public override void WriteJson(JsonWriter writer, Version value, JsonSerializer serializer)
        {
            writer.WriteValue(value?.ToString());
        }

        public override Version ReadJson(JsonReader reader, Type objectType, Version existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value as string;
            return string.IsNullOrEmpty(s) ? null : new Version(s);
        }
    }

    // Writes a decimal as its own digits, 52000 or 98000.00, as the other formats write it, where json would write
    // 52000.0, and reads it back as a decimal
    internal class DecimalJsonConverter : JsonConverter<decimal>
    {
        public override void WriteJson(JsonWriter writer, decimal value, JsonSerializer serializer)
        {
            writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
        }

        public override decimal ReadJson(JsonReader reader, Type objectType, decimal existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
        }
    }

    // Writes a double or a float as the other formats write it, the shortest text that reads back as the same number, 3
    // and not 3.0, and -0 for a negative zero; NaN and Infinity are written as json writes them, as text
    internal class DoubleJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(double) == objectType || typeof(float) == objectType;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            string text = value is double number ? ConverterHelper.NumberText(number) : ConverterHelper.NumberText((float)value);
            if (true == char.IsDigit(text[text.Length - 1]))
                writer.WriteRawValue(text);
            else
                writer.WriteValue(text);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return typeof(float) == objectType ? (object)Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture) : Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
        }
    }

    // Writes a BigInteger as a string and reads it back from one.
    public class BigIntegerJsonConverter : JsonConverter<BigInteger>
    {
        public override void WriteJson(JsonWriter writer, BigInteger value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override BigInteger ReadJson(JsonReader reader, Type objectType, BigInteger existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var s = reader.Value as string;
            return string.IsNullOrEmpty(s) ? BigInteger.Zero : BigInteger.Parse(s);
        }
    }
}
