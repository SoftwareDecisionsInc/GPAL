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
using System.Drawing;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class GPALElementConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(GPALElement) || objectType == typeof(List<GPALElement>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token;
            try
            {
                token = JToken.Load(reader);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to load JSON token", null, GPALObjectType.None, ex);
                return objectType == typeof(List<GPALElement>) ? new List<GPALElement>() : null;
            }

            if (token.Type == JTokenType.Array)
            {
                // Handle List<GPALElement>
                var jsonArray = (JArray)token;
                var result = new List<GPALElement>();

                //GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Processing JSON array with [{jsonArray.Count}] elements");

                foreach (JToken maybeWrapped in jsonArray)
                {
                    JToken payload = maybeWrapped;

                    // 1. Unwrap extra array level if present (common for single-element queries)
                    if (maybeWrapped.Type == JTokenType.Array && maybeWrapped.Children().Count() == 1)
                    {
                        payload = maybeWrapped.First;
                        //GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, "Unwrapped extra array layer");
                    }

                    GPALElement element = null;

                    if (payload.Type == JTokenType.Object)
                    {
                        // Normal structured object > deserialize as usual
                        try
                        {
                            element = DeserializeSingleElement((JObject)payload, serializer);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION,
                                "Normal object failed: " + ex.Message);
                        }
                    }
                    else if (payload.Type == JTokenType.String)
                    {
                        string raw = payload.Value<string>();
                        if (string.IsNullOrWhiteSpace(raw))
                            continue;

                        // the extension sends each element as its own JSON string
                        if (raw.TrimStart().StartsWith("{"))
                        {
                            try
                            {
                                var obj = JObject.Parse(raw);
                                element = DeserializeSingleElement(obj, serializer);
                            }
                            catch (JsonReaderException jex)
                            {
                                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING,
                                    "Element string failed parse: " + jex.Message);
                            }
                        }
                    }

                    // Final check — only add if we have something useful
                    if (element != null && element.Attributes != null && element.Attributes.Count > 0)
                    {
                        // commented: fired per element during bulk deserialize
                        //string tag = element.Attributes.ContainsKey("tagName")
                        //    ? element.Attributes["tagName"].ToString()
                        //    : "unknown";

                        //GPAL.PublishSimpleEvent(Enums.GPALEventType.DEEPDEBUG,
                        //    "Added element - tag: " + tag + ", attributes: " + element.Attributes.Count);

                        result.Add(element);
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING,
                            "Dropped item — no valid element created");
                    }
                }

                return result;
            }
            else if (token.Type == JTokenType.Object)
            {
                // Handle single GPALElement
                try
                {
                    var element = DeserializeSingleElement((JObject)token, serializer);
                    if (element != null && element.Attributes != null)
                    {
                        return element;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }
            else if (token.Type == JTokenType.String)
            {
                // Handle single double-serialized GPALElement
                try
                {
                    var jsonString = token.Value<string>();
                    var parsedObject = JObject.Parse(jsonString);
                    var element = DeserializeSingleElement(parsedObject, serializer);
                    if (element != null && element.Attributes != null)
                    {
                        return element;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch
                {
                    return objectType == typeof(List<GPALElement>) ? new List<GPALElement>() : null;
                }
            }

            return objectType == typeof(List<GPALElement>) ? new List<GPALElement>() : null;
        }
        private GPALElement DeserializeSingleElement(JObject jsonObject, JsonSerializer serializer)
        {
            var gpalelement = new GPALElement();

            // Deserialize Attributes dictionary
            if (jsonObject["Attributes"] != null)
            {
                var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var attributesJson = jsonObject["Attributes"] as JObject;

                if (attributesJson != null)
                {
                    foreach (var prop in attributesJson.Properties())
                    {
                        try
                        {
                            switch (prop.Name.ToLower())
                            {
                                case "location":
                                    var location = prop.Value.ToObject<Point>(serializer);
                                    attributes["location"] = location;
                                    // commented: fired per element during bulk deserialize; the eager string build (and its stack capture) cost seconds over 90+ elements
                                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized location: ([{location.X}], [{location.Y}])");
                                    break;
                                case "size":
                                    var size = prop.Value.ToObject<Size>(serializer);
                                    attributes["size"] = size;
                                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized size: ([{size.Width}], [{size.Height}])");
                                    break;
                                case "boundingrect":
                                    var boundingRect = prop.Value.ToObject<ClientRectangle>(serializer);
                                    attributes["boundingRect"] = boundingRect;
                                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized boundingRect: (width=[{boundingRect.Width}], height=[{boundingRect.Height}])");
                                    break;
                                case "xpaths":
                                    var xpaths = prop.Value.ToObject<List<XPathInfo>>(serializer);
                                    attributes["xpaths"] = xpaths.FindAll(x => x != null).ToArray();
                                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized attributes['xpaths']");
                                    break;
                                default:
                                    attributes[prop.Name] = prop.Value.ToObject<object>(serializer);
                                    // commented: fired per PROPERTY per element; {prop.Value} also re-serializes the token to a string every time
                                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized property [{prop.Name}]: [{prop.Value}]");
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize property [{prop.Name}]", null, GPALObjectType.None, ex);
                        }
                    }
                    gpalelement.Attributes = attributes;
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Attributes property is not a valid JObject");
                }
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Attributes property is missing or null");
            }

            // Deserialize ElementHandle
            if (jsonObject["ElementHandle"] != null)
            {
                try
                {
                    gpalelement.ElementHandle = jsonObject["ElementHandle"].ToObject<string>(serializer);
                    //GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Deserialized ElementHandle: [{gpalelement.ElementHandle}]");
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize ElementHandle", null, GPALObjectType.None, ex);
                }
            }

            return gpalelement;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Serialization not implemented");
        }

        public override bool CanWrite => false;
    }

    internal class PointConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Point);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                JToken token = JToken.Load(reader);
                if (token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    int x = obj["X"]?.Value<int>() ?? 0;
                    int y = obj["Y"]?.Value<int>() ?? 0;
                    return new Point(x, y);
                }
                else if (token.Type == JTokenType.String)
                {
                    string[] parts = token.Value<string>().Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    {
                        return new Point(x, y);
                    }
                }
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Point format: [{token}]");
                return Point.Empty;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize Point", null, GPALObjectType.None, ex);
                return Point.Empty;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var point = (Point)value;
            writer.WriteValue($"{point.X},{point.Y}");
        }

        public override bool CanWrite => true;
    }

    internal class SizeConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Size);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                JToken token = JToken.Load(reader);
                if (token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    int width = obj["Width"]?.Value<int>() ?? 0;
                    int height = obj["Height"]?.Value<int>() ?? 0;
                    return new Size(width, height);
                }
                else if (token.Type == JTokenType.String)
                {
                    string[] parts = token.Value<string>().Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                    {
                        return new Size(width, height);
                    }
                }
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Size format: [{token}]");
                return Size.Empty;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize Size", null, GPALObjectType.None, ex);
                return Size.Empty;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var size = (Size)value;
            writer.WriteValue($"{size.Width},{size.Height}");
        }

        public override bool CanWrite => true;
    }
}
