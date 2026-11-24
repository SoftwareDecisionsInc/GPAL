// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerallyPositive
{
    class CustomJsonConverter: CustomCreationConverter<IDictionary<dynamic, dynamic>>
    {
        // the json text being read and where each of its lines starts, so a number is read from the text it was written as
        private readonly string source;
        private readonly List<int> lineStarts = new List<int> { 0 };

        public CustomJsonConverter()
        {
        }

        /// <summary>
        /// True when the json nests deeper than the converter reads, ConverterHelper.NestingLimit levels; the deeper part
        /// was passed over
        /// </summary>
        internal bool TooDeep { get; private set; }

        /// <param name="source">The json text being read; a number in it keeps the digits it was written with</param>
        public CustomJsonConverter(string source)
        {
            this.source = source;
            // a line ends at \n, \r\n or a \r alone, as the json reader counts lines
            for (int charIdx = 0; charIdx < (source?.Length ?? 0); charIdx++)
                if ('\n' == source[charIdx] || ('\r' == source[charIdx] && (charIdx + 1 == source.Length || '\n' != source[charIdx + 1])))
                    lineStarts.Add(charIdx + 1);
        }

        public override IDictionary<dynamic, dynamic> Create(Type objectType)
        {
            return new Dictionary<dynamic, dynamic>();
        }

        public override bool CanConvert(Type objectType)
        {
            // in addition to handling IDictionary<string, object>
            // we want to handle the deserialization of dict value
            // which is of type object
            return objectType == typeof(object) || base.CanConvert(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // A number keeps the digits it was written with: 1.50 or 52000.00 is a decimal, as yaml and xml read it, where
            // a double would make it 1.5; a number a decimal cannot hold as written, such as 1E-10 or 5E-324, stays a
            // double. -0 is a negative zero, where the reader makes it 0. The number's text ends where the reader stands
            string written = null;
            if ((JsonToken.Float == reader.TokenType || JsonToken.Integer == reader.TokenType) && null != source
                && reader is IJsonLineInfo position && true == position.HasLineInfo() && position.LineNumber <= lineStarts.Count)
            {
                int end = Math.Min(lineStarts[position.LineNumber - 1] + position.LinePosition, source.Length);
                int start = end;
                while (0 < start && 0 <= "+-.eE0123456789".IndexOf(source[start - 1]))
                    start--;
                written = source.Substring(start, end - start);
            }
            decimal asWritten = 0;
            bool keepsDigits = JsonToken.Float == reader.TokenType && null != written
                && true == decimal.TryParse(written, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out asWritten)
                && asWritten.ToString(System.Globalization.CultureInfo.InvariantCulture) == written;

            object retVal;
            // json nested deeper than that is passed over by the reader, which does not go a level deeper for each level,
            // so the read stops with an error and not a stack overflow
            if (ConverterHelper.NestingLimit < reader.Depth && (JsonToken.StartObject == reader.TokenType || JsonToken.StartArray == reader.TokenType))
            {
                TooDeep = true;
                reader.Skip();
                retVal = null;
            }
            else if (JsonToken.StartObject == reader.TokenType || JsonToken.Null == reader.TokenType)
                retVal = base.ReadJson(reader, objectType, existingValue, serializer);
            //if it's an array serialize it as a list of dictionaries
            else if (JsonToken.StartArray == reader.TokenType)
                retVal = serializer.Deserialize(reader, typeof(List<object>));
            else if (true == keepsDigits)
                retVal = asWritten;
            // a zero written with a minus, such as -0, -0.0 or -0e0, is a negative zero, which .NET Framework reads as 0
            else if ((JsonToken.Integer == reader.TokenType || JsonToken.Float == reader.TokenType) && null != written
                && true == System.Text.RegularExpressions.Regex.IsMatch(written, @"^-0*\.?0*([eE][-+]?[0-9]+)?$") && true == written.Contains("0"))
                retVal = -0.0;
            // if the next token is not an object
            // then fall back on standard deserializer (strings, numbers etc.)
            else
                retVal = serializer.Deserialize(reader);
            return retVal;
        }
    }
}

