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
    public partial class PuppeteerCommunicator
    {
        /// <summary>
        /// Maps a character to the corresponding CDP/DOM "code" value (e.g. <c>KeyA</c>, <c>Digit1</c>,
        /// <c>Comma</c>) used for synthesizing keyboard events.
        /// </summary>
        /// <param name="c">The character to map to a key code.</param>
        /// <returns>The CDP key code string for the character, or the character itself if no specific mapping exists.</returns>
        private string GetKeyCode(char c)
        {
            char upper = char.ToUpperInvariant(c);

            if (upper >= 'A' && upper <= 'Z')
            {
                return "Key" + upper;
            }

            if (char.IsDigit(c))
            {
                return "Digit" + c;
            }

            switch (c)
            {
                case '`':
                case '~':
                    return "Backquote";

                case '-':
                case '_':
                    return "Minus";

                case '=':
                case '+':
                    return "Equal";

                case '[':
                case '{':
                    return "BracketLeft";

                case ']':
                case '}':
                    return "BracketRight";

                case '\\':
                case '|':
                    return "Backslash";

                case ';':
                case ':':
                    return "Semicolon";

                case '\'':
                case '"':
                    return "Quote";

                case ',':
                case '<':
                    return "Comma";

                case '.':
                case '>':
                    return "Period";

                case '/':
                case '?':
                    return "Slash";

                case ' ':
                    return "Space";

                case '\t':
                    return "Tab";

                case '\r':
                case '\n':
                    return "Enter";

                default:
                    return c.ToString();  // fallback
            }
        }

        // Implementation for "fillin" helper
        /// <summary>
        /// Fills a form field with the given text, trying multiple strategies in order of speed/reliability:
        /// bulk text insertion, direct JS value assignment with synthetic events, and finally per-character key events.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to type into the element.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the text was successfully filled in, otherwise false.</returns>
        /// <example>
        /// <code>
        /// await communicator.FillIn(inputObjectId, "hello@example.com");
        /// </code>
        /// </example>
        public async Task<dynamic> FillIn(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Input.insertText and key events go to whatever has focus. An element that cannot take focus, such as one
            // that is not rendered, would send its text into the element that still has it, so for that element only
            // the direct fill is tried
            bool focused = await IsElementFocused(objectId, sessionId).ConfigureAwait(false);

            // Input.insertText (faster bulk insert)
            bool success = true == focused && await TryInsertText(objectId, text, sessionId).ConfigureAwait(false);

            // JS direct set + synthetic events
            if (false == success)
                success = await TryJsDirectFill(objectId, text, sessionId).ConfigureAwait(false);

            // Per-char key events (most reliable for validation), once the element has focus
            if (false == success)
            {
                await FocusElement(objectId, sessionId).ConfigureAwait(false);

                if (true == await IsElementFocused(objectId, sessionId).ConfigureAwait(false))
                    success = await TryTypeWithKeyEvents(objectId, text, delayMs, sessionId).ConfigureAwait(false);
            }

            if (false == success)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fill element [{objectId}] with [{text}] using all methods.");

            return success;
        }

        /// <summary>
        /// Attempts to fill an element by dispatching synthetic keyDown/keyUp events for each character,
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to type, character by character.</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. If 0, a small fixed default is used since this path is inherently per-character.</param>
        /// <param name="sessionId">CDP session id to dispatch the key events on.</param>
        /// <returns>Task that resolves to true if the element's value matches the typed text afterwards, otherwise false.</returns>
        private async Task<bool> TryTypeWithKeyEvents(string objectId, string text, int delayMs, string sessionId)
        {
            try
            {
                await DispatchTextCharByChar(text, 0 < delayMs ? delayMs : 50, sessionId).ConfigureAwait(false);

                // After typing, verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    // Final blur to trigger any validation/button enable
                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
        }

        /// <summary>
        /// Attempts to fill an element using a single Input.insertText call (fast bulk insert),
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to insert.</param>
        /// <param name="sessionId">CDP session id to dispatch the insert on.</param>
        /// <returns>Task that resolves to true if the element's value matches the inserted text afterwards, otherwise false.</returns>
        private async Task<bool> TryInsertText(string objectId, string text, string sessionId)
        {
            try
            {
                // Bulk insert
                await SendCommand(DevToolsMethods.InputInsertText, new { text }, sessionId).ConfigureAwait(false);

                // Verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    // Input.insertText fires input but no key events, so a page listening for keyup (to enable a
                    // button, say) never hears the typing. Release the last character's key, as a person's last
                    // keystroke would; a keyUp alone inserts nothing, so the value stays as it is.
                    if (0 < text.Length)
                    {
                        char last = text[text.Length - 1];
                        int vkCode = char.IsLetterOrDigit(last) ? (int)char.ToUpperInvariant(last) : (int)last;

                        await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                        {
                            type = "keyUp",
                            key = last.ToString(),
                            windowsVirtualKeyCode = vkCode,
                            nativeVirtualKeyCode = vkCode
                        }, sessionId).ConfigureAwait(false);
                    }

                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
        }

        /// <summary>
        /// Attempts to fill an element by directly setting its "value" property via JavaScript
        /// (bypassing framework value setters where possible) and dispatching "input"/"change" events,
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to assign to the element's value.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        /// <returns>Task that resolves to true if the element's value matches the assigned text afterwards, otherwise false.</returns>
        private async Task<bool> TryJsDirectFill(string objectId, string text, string sessionId)
        {
            try
            {
                // Set .value through the native setter, then fire input and change
                var js = @"
                        function(value) {
                            this.value = value;

                            // Trigger everything frameworks listen to
                            const desc = Object.getOwnPropertyDescriptor(this, 'value') || 
                                          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value') ||
                                          Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');

                            // Bypass any framework value setters that block direct assignment
                            if (desc && desc.set) {
                                desc.set.call(this, value);
                            }

                            this.dispatchEvent(new Event('input', { bubbles: true }));
                            this.dispatchEvent(new Event('change', { bubbles: true }));
                        }";

                await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
                {
                    objectId,
                    functionDeclaration = js,
                    arguments = new[] { new { value = text } },
                    contextId = CurrentContextId,
                    awaitPromise = true
                }, sessionId).ConfigureAwait(false);

                // Verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
        }

        // Helper: Focus
        /// <summary>
        /// Helper that calls <c>focus()</c> on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        internal async Task FocusElement(string objectId, string sessionId)
        {
            await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() { this.focus(); }"
            }, sessionId).ConfigureAwait(false);
        }

        // Helper: Blur
        /// <summary>
        /// Helper that calls <c>blur()</c> on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        private async Task BlurElement(string objectId, string sessionId)
        {
            await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() { this.blur(); }"
            }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-append"
        /// <summary>
        /// Appends text to the end of an element's current value, then fills it in using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to append after the element's existing value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInAppend(inputObjectId, "-suffix");
        /// </code>
        /// </example>
        public async Task FillInAppend(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var currentResp = await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this.value || ''; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string current = currentResp?.result?.value?.ToString() ?? "";
            await FillIn(objectId, current + text, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-insert"
        /// <summary>
        /// Inserts text before an element's current value (prepending it), then fills it in using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to insert before the element's existing value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInInsert(inputObjectId, "prefix-");
        /// </code>
        /// </example>
        public async Task FillInInsert(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var currentResp = await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this.value || ''; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string current = currentResp?.result?.value?.ToString() ?? "";
            await FillIn(objectId, text + current, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-overwrite"
        /// <summary>
        /// Replaces an element's current value entirely with the given text using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text that will become the element's new value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInOverwrite(inputObjectId, "new value");
        /// </code>
        /// </example>
        public async Task FillInOverwrite(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            await FillIn(objectId, text, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fire-change-event"
        /// <summary>
        /// Dispatches a "change" event on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the Runtime.callFunctionOn command.</returns>
        public async Task<dynamic> FireChangeEvent(string objectId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() {{ this.dispatchEvent(new Event('change')); }}"
            }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "focus"
        /// <summary>
        /// Sets browser focus on the element identified by the given backend node id, using DOM.focus.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element to focus.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        public async Task Focus(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            // DOM.focus takes the backend id, which lasts as long as the element; a node id can go stale
            await SendCommand<object>(DevToolsMethods.DOMFocus, new { backendNodeId }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "press-modifier-key"
        /// <summary>
        /// CDP modifier flags pressed with <see cref="PressModifierKey"/> and not yet released. The browser keeps no
        /// modifier state of its own, so every click, drag, pointer move and key GPAL dispatches carries these.
        /// </summary>
        internal int HeldModifiers { get; private set; } = 0;

        // CDP modifier flag, DOM key, DOM code and virtual key code of each modifier key
        private static readonly (int flag, string key, string code, int vk)[] MODIFIER_KEYS =
        {
            (1, "Alt", "AltLeft", 0x12),
            (2, "Control", "ControlLeft", 0x11),
            (4, "Meta", "MetaLeft", 0x5B),
            (8, "Shift", "ShiftLeft", 0x10)
        };

        /// <summary>
        /// Presses and holds the given modifier key(s): a key down for each one not already held, and every later
        /// click, drag, pointer move and key carries it until <see cref="ReleaseModifierKey"/>.
        /// Implementation for the "press-modifier-key" workflow action.
        /// </summary>
        /// <param name="modifierKeys">Bitmask of CDP modifier flags to press (e.g. Alt, Ctrl, Meta, Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous key-down operation.</returns>
        public async Task PressModifierKey(int modifierKeys, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            foreach (var modifier in MODIFIER_KEYS)
                if (0 != (modifierKeys & modifier.flag) && 0 == (HeldModifiers & modifier.flag))
                {
                    HeldModifiers |= modifier.flag;
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "rawKeyDown",
                        key = modifier.key,
                        code = modifier.code,
                        windowsVirtualKeyCode = modifier.vk,
                        nativeVirtualKeyCode = modifier.vk,
                        location = 1,
                        modifiers = HeldModifiers
                    }, sessionId).ConfigureAwait(false);
                }
        }

        // Implementation for "release-modifier-key"
        /// <summary>
        /// Releases the given modifier key(s): a key up for each one held via <see cref="PressModifierKey"/>.
        /// Implementation for the "release-modifier-key" workflow action.
        /// </summary>
        /// <param name="modifierKeys">Bitmask of CDP modifier flags to release (e.g. Alt, Ctrl, Meta, Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous key-up operation.</returns>
        public async Task ReleaseModifierKey(int modifierKeys, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            foreach (var modifier in MODIFIER_KEYS)
                if (0 != (modifierKeys & modifier.flag) && 0 != (HeldModifiers & modifier.flag))
                {
                    HeldModifiers &= ~modifier.flag;
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "keyUp",
                        key = modifier.key,
                        code = modifier.code,
                        windowsVirtualKeyCode = modifier.vk,
                        nativeVirtualKeyCode = modifier.vk,
                        location = 1,
                        modifiers = HeldModifiers
                    }, sessionId).ConfigureAwait(false);
                }
        }

        // Implementation for "send-key"
        /// <summary>
        /// Dispatches a full key press sequence (keydown, optional char, keyup) for the given key via CDP,
        /// using "\r" as the char payload for the Enter key so forms submit correctly, and the literal
        /// character for single-character keys.
        /// Implementation for the "send-key" workflow action.
        /// </summary>
        /// <param name="key">Key value (e.g. "Enter", "a", "ArrowDown") as used by the CDP key event.</param>
        /// <param name="code">Physical key code (e.g. "Enter", "KeyA", "ArrowDown") as used by the CDP key event.</param>
        /// <param name="vkCode">Windows virtual key code corresponding to the key.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true if all key events were dispatched successfully, or false if an exception occurred.</returns>
        public async Task<dynamic> SendKey(string key, string code, int vkCode, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // a single character key reads upper case while Shift is held, as a real Shift press reports it
            if (1 == key.Length && 0 != (HeldModifiers & 8))
                key = key.ToUpperInvariant();

            try
            {
                // For forms to submit on Enter, Chrome requires the text property to be "\r".
                // Otherwise, printable characters use their literal string value.
                // A held Alt, Control or Windows key makes the key a shortcut, so no character is typed
                string textPayload = 0 != (HeldModifiers & 7) ? null
                    : (code == "Enter") ? "\r"
                    : (key.Length == 1 ? key : null);

                // 1. Dispatch Key Down
                var keyDownParams = new
                {
                    type = "rawKeyDown",
                    key = key,
                    code = code,
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode,
                    modifiers = HeldModifiers
                };
                await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, keyDownParams, sessionId).ConfigureAwait(false);

                // 2. Dispatch Char (Only if the key sequence produces an explicit input character)
                if (textPayload != null)
                {
                    var charParams = new
                    {
                        type = "char",
                        text = textPayload,
                        unmodifiedText = textPayload,
                        key = key,
                        code = code,
                        windowsVirtualKeyCode = vkCode,
                        nativeVirtualKeyCode = vkCode,
                        modifiers = HeldModifiers
                    };
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, charParams, sessionId).ConfigureAwait(false);
                }

                // 3. Dispatch Key Up
                var keyUpParams = new
                {
                    type = "keyUp",
                    key = key,
                    code = code,
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode,
                    modifiers = HeldModifiers
                };
                await SendCommand<dynamic>(DevToolsMethods.InputDispatchKeyEvent, keyUpParams, sessionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"SendKey execution failed for Code [{code}]", this, GPALObjectType.PuppeteerCommunicator, ex);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Maps a key name (e.g. "Enter", "Tab", "ArrowUp", "F1") to its corresponding Windows virtual key code.
        /// Returns 0 for letters, digits, symbols, and any unrecognized key name.
        /// </summary>
        /// <param name="key">Key name to map (case-insensitive).</param>
        /// <returns>The Windows virtual key code for the key, or 0 if unknown or not a special key.</returns>
        private int GetVirtualKeyCode(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;

            string k = key.ToLowerInvariant();

            switch (k)
            {
                case "enter":
                case "return":
                    return 0x0D;

                case "tab":
                    return 0x09;

                case "backspace":
                    return 0x08;

                case "delete":
                case "del":
                    return 0x2E;

                case "escape":
                case "esc":
                    return 0x1B;

                case "space":
                    return 0x20;

                case "arrowup":
                case "up":
                    return 0x26;

                case "arrowdown":
                case "down":
                    return 0x28;

                case "arrowleft":
                case "left":
                    return 0x25;

                case "arrowright":
                case "right":
                    return 0x27;

                case "home":
                    return 0x24;

                case "end":
                    return 0x23;

                case "pageup":
                    return 0x21;

                case "pagedown":
                    return 0x22;

                case "insert":
                    return 0x2D;

                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;

                default:
                    return 0;   // letters, numbers, symbols etc.
            }
        }

        // Implementation for "send-string"
        /// <summary>
        /// Sends the given text at the current focus/cursor position. With <paramref name="delayMs"/> equal to 0
        /// (the default), the whole string is inserted atomically via CDP's <c>Input.insertText</c>. When
        /// <paramref name="delayMs"/> is greater than 0, the text is dispatched character-by-character via
        /// <see cref="DispatchTextCharByChar"/>, sleeping <paramref name="delayMs"/> milliseconds between characters.
        /// Implementation for the "send-string" workflow action (equivalent to "fill-in").
        /// </summary>
        /// <param name="text">Text to insert.</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous text insertion operation.</returns>
        public async Task SendString(string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            if (0 == delayMs)
                await SendCommand<object>(DevToolsMethods.InputInsertText, new { text }, sessionId).ConfigureAwait(false);
            else
                await DispatchTextCharByChar(text, delayMs, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatches the given text one character at a time via CDP's <c>Input.dispatchKeyEvent</c>
        /// (keyDown followed by keyUp for each character), sleeping a random duration between
        /// Math.Min(25, <paramref name="delayMs"/>) and <paramref name="delayMs"/> milliseconds between
        /// characters. Shared by <see cref="SendString"/> and <see cref="TryTypeWithKeyEvents"/>.
        /// </summary>
        /// <param name="text">Text to type, character by character.</param>
        /// <param name="delayMs">Ceiling in milliseconds for the randomized inter-character delay.</param>
        /// <param name="sessionId">CDP session id to dispatch the key events on.</param>
        private async Task DispatchTextCharByChar(string text, int delayMs, string sessionId)
        {
            bool first = true;
            foreach (char c in text)
            {
                if (false == first && 0 < delayMs)
                    await Task.Delay(HardwareHelper.GetTypingDelay(delayMs)).ConfigureAwait(false);

                string key = c.ToString();
                int vkCode = (int)c; // the character code, used as the virtual key code

                await SendCommand(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyDown",
                    text = key,                // for printable chars
                    unmodifiedtext = key,
                    key = key,
                    code = GetKeyCode(c),      // e.g. "KeyA" for 'a'/'A'
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode
                }, sessionId).ConfigureAwait(false);

                await SendCommand(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyUp",
                    key = key,
                    code = GetKeyCode(c),
                    windowsVirtualKeyCode = vkCode
                }, sessionId).ConfigureAwait(false);

                first = false;
            }
        }

        // Implementation for "scroll-element"
        /// <summary>
        /// Sets the value of the element matching the given CSS selector and dispatches "input" and "change"
        /// events so the page reacts to the new value (e.g. for range/slider inputs).
        /// Implementation for the "set-range" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector identifying the target element.</param>
        /// <param name="rangeValue">New value to assign (inserted directly into the generated JavaScript expression).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the JavaScript evaluation.</returns>
        public async Task<dynamic> SetRange(string elementId, string rangeValue, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new
                {
                    contextId = CurrentContextId,
                    expression = $@"var s = document.querySelector('{elementId}'); s.value = {rangeValue}; s.dispatchEvent(new Event('input', {{bubbles: true}})); s.dispatchEvent(new Event('change', {{bubbles: true}}));"
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "set-value-from-element"
        /// <summary>
        /// Copies the value from a source element into a destination element by setting
        /// <c>destElement.value = srcElement.value</c> via JavaScript evaluation, then dispatches
        /// "input" and "change" events on the destination so the page reacts to the new value.
        /// Implementation for the "set-value-from-element" workflow action.
        /// </summary>
        /// <param name="srcCss">CSS selector identifying the source element whose value is read.</param>
        /// <param name="destCss">CSS selector identifying the destination element whose value is set.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the JavaScript evaluation.</returns>
        public async Task<dynamic> SetValueFromElement(string srcCss, string destCss, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new
                {
                    contextId = CurrentContextId,
                    expression = $@"var s = document.querySelector('{srcCss}'); var d = document.querySelector('{destCss}'); d.value = s.value; d.dispatchEvent(new Event('input', {{bubbles: true}})); d.dispatchEvent(new Event('change', {{bubbles: true}}));"
                }, sessionId).ConfigureAwait(false);
        }
    }
}
