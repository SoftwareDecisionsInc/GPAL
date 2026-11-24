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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    internal static class DevToolsCommandBuilder
    {
        // Complete list of CDP DOM methods that require a nodeId parameter
        // Based on Chrome DevTools Protocol (version 1.3, stable as of Chrome 126+)
        internal static readonly HashSet<string> MethodsRequiringNodeId = new HashSet<string>
                            {
                                // DOM domain methods that require nodeId
                                "DOM.copyTo",
                                "DOM.describeNode",
                                "DOM.focus",
                                "DOM.getAttributes",
                                "DOM.getBoxModel",
                                "DOM.getContentQuads",
                                "DOM.getFlattenedDocument",
                                "DOM.getNodeForLocation",
                                "DOM.getOuterHTML",
                                "DOM.getRelayoutBoundary",
                                "DOM.moveTo",
                                "DOM.querySelector",
                                "DOM.querySelectorAll",
                                "DOM.removeAttribute",
                                "DOM.removeNode",
                                "DOM.requestNode",
                                "DOM.setAttributeValue",
                                "DOM.setAttributesAsText",
                                "DOM.setNodeName",
                                "DOM.setNodeValue",
                                "DOM.setOuterHTML",
                                "DOM.collectClassNames",
                                "DOM.getNodeStackTraces",
                                "DOM.scrollIntoViewIfNeeded",
                                // Accessibility domain methods that require nodeId
                                "Accessibility.getPartialAXTree",
                                "Accessibility.getFullAXTree",
                                // CSS domain methods that require nodeId
                                "CSS.getComputedStyleForNode",
                                "CSS.getInlineStylesForNode",
                                "CSS.getMatchedStylesForNode",
                                "CSS.getBackgroundColors",
                            };

        public static string BuildCommand<TParameters>(
                DevToolsMethods method,
                TParameters parameters,
                long commandId,
                string sessionId = null,
                int docRootId = 1)
        {
            string methodString = GetMethodString(method);
            object finalParameters = parameters;

            // Check if the method requires a nodeId and parameters are provided
            if (MethodsRequiringNodeId.Contains(methodString) && parameters != null)
            {
                // Convert parameters to dictionary for manipulation
                IDictionary<string, object> paramDict;
                if (parameters is IDictionary<string, object> dict)
                {
                    paramDict = dict;
                }
                else
                {
                    // Handle anonymous objects
                    paramDict = parameters.GetType()
                        .GetProperties()
                        .ToDictionary(p => p.Name, p => p.GetValue(parameters));
                }

                finalParameters = paramDict;
            }

            string paramsString = "";
            if (finalParameters != null)
            {
                IDictionary<string, object> paramDict;
                if (finalParameters is IDictionary<string, object> dict)
                {
                    paramDict = dict;
                }
                else
                {
                    paramDict = finalParameters.GetType()
                        .GetProperties()
                        .ToDictionary(p => p.Name, p => p.GetValue(finalParameters));
                }

                var filteredDict = paramDict.Where(kvp => kvp.Value != null)
                                           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (filteredDict.Count > 0)
                {
                    paramsString = $", \"params\": {JsonConvert.SerializeObject(filteredDict)}";
                }
            }

            string sessionString = sessionId != null ? $"\"sessionId\": \"{sessionId}\"," : "";
            string command = $"{{\"id\": {commandId}, {sessionString} \"method\": \"{methodString}\"{paramsString}}}";

            // Logs every generated command at DEEPDEBUG
            GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Generated command: [{command}]");

            return command;
        }

        /// <summary>
        /// Gets the wire-format CDP method string (e.g. "DOM.querySelector") for the given <see cref="DevToolsMethods"/> value,
        /// using its <see cref="CommandAttribute"/> if present, or falling back to replacing underscores with dots.
        /// </summary>
        /// <param name="method">The DevTools method enum value.</param>
        /// <returns>The CDP wire-format method string.</returns>
        internal static string GetMethodString(DevToolsMethods method)
        {
            var attr = method.GetAttributeOfType<CommandAttribute>();
            return attr?.CommandString ?? method.ToString().Replace('_', '.');
        }
    }
}
