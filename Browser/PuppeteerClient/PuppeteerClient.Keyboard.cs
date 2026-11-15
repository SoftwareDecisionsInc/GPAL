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
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Configure this client to append text to the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInAppend(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInAppend);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to insert text at the start of the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInInsert(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInInsert);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to overwrite the text of the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInOverwrite(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInOverwrite);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to press and hold the given modifier key(s).
        /// </summary>
        /// <param name="modifierKeys">Modifier key(s) to press and hold</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PressModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.PressModifierKey);
            SetParameter("modifierKeys", GetModifierBitmask(modifierKeys));
            return this;
        }

        /// <summary>
        /// Configure this client to release the given modifier key(s).
        /// </summary>
        /// <param name="modifierKeys">Modifier key(s) to release</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.ReleaseModifierKey);
            SetParameter("modifierKeys", GetModifierBitmask(modifierKeys));
            return this;
        }

        // Helper method to convert ModifierKeys to CDP modifiers bitmask
        /// <summary>
        /// Converts GPAL ModifierKeys flags to the equivalent Chrome DevTools Protocol modifiers bitmask.
        /// </summary>
        /// <param name="modifierKeys">The modifier keys to convert</param>
        /// <returns>The CDP modifiers bitmask</returns>
        public int GetModifierBitmask(ModifierKeys modifierKeys)
        {
            int bitmask = 0;
            if (modifierKeys.HasFlag(ModifierKeys.Alt))
                bitmask |= 1; // CDP Alt
            if (modifierKeys.HasFlag(ModifierKeys.Control))
                bitmask |= 2; // CDP Ctrl
            if (modifierKeys.HasFlag(ModifierKeys.Windows)) // Maps to Meta/Command
                bitmask |= 4; // CDP Meta
            if (modifierKeys.HasFlag(ModifierKeys.Shift))
                bitmask |= 8; // CDP Shift
            return bitmask;
        }
        /// <summary>
        /// Converts a Chrome DevTools Protocol modifiers bitmask back to a human-readable, plus-separated list of modifier names.
        /// </summary>
        /// <param name="bitmask">The CDP modifiers bitmask</param>
        /// <returns>The modifier names joined with "+", or "None" if no bits are set</returns>
        public string GetModifiersString(int bitmask)
        {
            var modifiers = new List<string>();

            if ((bitmask & 1) != 0) // Check for Alt
                modifiers.Add("Alt");
            if ((bitmask & 2) != 0) // Check for Control
                modifiers.Add("Control");
            if ((bitmask & 4) != 0) // Check for Windows/Meta
                modifiers.Add("Windows");
            if ((bitmask & 8) != 0) // Check for Shift
                modifiers.Add("Shift");

            return modifiers.Count > 0 ? string.Join("+", modifiers) : "None";
        }

        /// <summary>
        /// Configure this client to type the given text into the focused element.
        /// </summary>
        /// <param name="text">The text to type</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically; greater than 0 dispatches character-by-character with this delay between characters.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SendString(string text, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.SendString);
            WithText(text);
            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to send a key press for the given virtual key code, translating it to the corresponding DOM key and code.
        /// </summary>
        /// <param name="vkcode">The virtual key code to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SendKey(byte vkcode)
        {
            WithEndpoint(ApiEndpoint.SendKey);

            // A key with no DOM name is sent by its virtual key code alone, as "Unidentified"
            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkcode, out string key, out string code))
            {
                SetParameter("key", key);
                SetParameter("code", code);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Virtual key [0x{vkcode:X2}] has no DOM key name, so it is sent as [Unidentified]", this, GPALObjectType.PuppeteerClient);

            SetParameter("vk", (int)vkcode);
            return this;
        }

        /// <summary>
        /// Configure this client to fire a DOM change event on the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution FireChangeEvent(string selector)
        {
            WithEndpoint(ApiEndpoint.FireChangeEvent);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to set keyboard focus on the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Focus(string selector)
        {
            WithEndpoint(ApiEndpoint.Focus);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to set the value of a range input element; combine with WithRangeValue to specify the value.
        /// </summary>
        /// <param name="selector">CSS selector of the range element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerRangeValue SetRange(string selector)
        {
            WithEndpoint(ApiEndpoint.SetRange);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Sets the value of the destination element identified by <paramref name="destSelector"/> from the
        /// source element identified by the preceding <see cref="WithCss(string)"/> or
        /// <see cref="WithElementId(string)"/> call.
        /// <example>client.WithElementId("#source").SetValueFrom("#destination").Execute();</example>
        /// </summary>
        /// <param name="destSelector">CSS selector identifying the destination element whose value is set</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetValueFrom(string destSelector)
        {
            WithEndpoint(ApiEndpoint.SetValueFromElement);
            SetParameter("destSelector", destSelector);
            return this;
        }
    }
}
