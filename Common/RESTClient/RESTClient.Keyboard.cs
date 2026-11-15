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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
        /// <summary>
        /// Fires a change event on an element by ID
        /// <example>client.FireChangeEvent("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution FireChangeEvent(string elementId)
        {
            WithEndpoint(ApiEndpoint.FireChangeEvent).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Focuses an element by ID
        /// <example>client.Focus("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution Focus(string elementId)
        {
            WithEndpoint(ApiEndpoint.Focus).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Starts setting an attribute of an element by ID
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRangeValue SetRange(string elementId)
        {
            WithEndpoint(ApiEndpoint.SetRange).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInAppend(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInAppend).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInInsert(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInInsert).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInOverwrite(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInOverwrite).WithElementId(elementId);
            return this;
        }
        /// <summary>
        /// Press the modifier keys specified.
        /// Keep pressed until a release is sent.
        /// </summary>
        /// <param name="modifierKeys"></param>
        /// <returns></returns>
        public IAllowRESTExecution PressModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.PressModifierKey);
            _parameters = null == _parameters
                ? new { modifierKeys }
                : MergeParameters(_parameters, new { modifierKeys });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Press the modifier keys specified.
        /// Keep pressed until a release is sent.
        /// </summary>
        /// <param name="modifierKeys"></param>
        /// <returns></returns>
        public IAllowRESTExecution ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.ReleaseModifierKey);
            _parameters = null == _parameters
                ? new { modifierKeys }
                : MergeParameters(_parameters, new { modifierKeys });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Sends a key
        /// <example>client.SendKey("Enter").Execute();</example>
        /// </summary>
        /// <param name="vkcode">The Windows virtual-key code of the key to send</param>
        /// <returns></returns>
        public IAllowRESTExecution SendKey(byte vkcode)
        {
            string key = null;
            string code = null;

            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkcode, out key, out code))
            {
                WithEndpoint(ApiEndpoint.SendKey);
                _parameters = null == _parameters
                    ? new { key }
                    : MergeParameters(_parameters, new { key });
                _parameters = MergeParameters(_parameters, new { code });
                _parameters = MergeParameters(_parameters, new { vk = code });
                WithHttpMethod("POST");
            }
            return this;
        }

        /// <summary>
        /// Sends a string
        /// <example>client.SendString("Hello").Execute();</example>
        /// </summary>
        /// <param name="text">The text to send</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically; greater than 0 dispatches character-by-character with this delay between characters.</param>
        /// <returns></returns>
        public IAllowRESTExecution SendString(string text, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.SendString);
            _parameters = null == _parameters
                ? new { text, typingDelay = delayMs }
                : MergeParameters(_parameters, new { text, typingDelay = delayMs });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the value of the destination element identified by <paramref name="destSelector"/> from the
        /// source element identified by the preceding <see cref="WithCss(string)"/> or
        /// <see cref="WithElementId(string)"/> call.
        /// <example>client.WithElementId("srcSelector").SetValueFrom("destSelector").Execute();</example>
        /// </summary>
        /// <param name="destSelector">CSS selector identifying the destination element whose value is set</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution SetValueFrom(string destSelector)
        {
            WithEndpoint(ApiEndpoint.SetValueFromElement);
            _parameters = null == _parameters
                ? new { destSelector }
                : MergeParameters(_parameters, new { destSelector });
            WithHttpMethod("POST");
            return this;
        }
    }
}
