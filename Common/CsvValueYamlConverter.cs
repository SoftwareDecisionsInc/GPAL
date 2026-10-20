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
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace GenerallyPositive
{
    // written by grok
    public class CsvValue
    {
        public string Value { get; set; }
        public bool WasQuoted { get; set; }

        public CsvValue(string value, bool wasQuoted)
        {
            Value = value;
            WasQuoted = wasQuoted;
        }
    }

    internal class CsvValueYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(CsvValue);

        public object ReadYaml(IParser parser, Type type)
        {
            var value = parser.Consume<Scalar>().Value;
            bool wasQuoted = value.StartsWith("\"") && value.EndsWith("\"");
            if (wasQuoted) value = value.Trim('"');
            return new CsvValue(value, wasQuoted);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var csvValue = (CsvValue)value;
            var scalarStyle = csvValue.WasQuoted ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain;
            emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, csvValue.Value, scalarStyle, !csvValue.WasQuoted, csvValue.WasQuoted));
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalarValue = parser.Consume<Scalar>().Value;
            bool wasQuoted = scalarValue.StartsWith("\"") && scalarValue.EndsWith("\"");
            if (wasQuoted) scalarValue = scalarValue.Trim('"');
            return new CsvValue(scalarValue, wasQuoted);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
        {
            var csvValue = (CsvValue)value;
            var scalarStyle = csvValue.WasQuoted ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain;
            emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, csvValue.Value, scalarStyle, !csvValue.WasQuoted, csvValue.WasQuoted));
        }
    }
}

