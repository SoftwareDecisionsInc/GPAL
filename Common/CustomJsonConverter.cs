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
            if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.Null)
                return base.ReadJson(reader, objectType, existingValue, serializer);

            //if it's an array serialize it as a list of dictionaries
            if (reader.TokenType == JsonToken.StartArray)
                return serializer.Deserialize(reader, typeof(List<object>));


            // if the next token is not an object
            // then fall back on standard deserializer (strings, numbers etc.)
            return serializer.Deserialize(reader);
        }
    }
}

