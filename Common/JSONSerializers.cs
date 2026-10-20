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
            string separator = value.Imaginary >= 0 ? "+" : "-";
            writer.WriteValue($"{value.Real.ToString(CultureInfo.InvariantCulture)}{separator}{Math.Abs(value.Imaginary).ToString(CultureInfo.InvariantCulture)}i");
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

    // BigInteger usually works fine in recent Json.NET versions (serialized as string for large values),
    // but if you hit issues with very large numbers, add this:
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
