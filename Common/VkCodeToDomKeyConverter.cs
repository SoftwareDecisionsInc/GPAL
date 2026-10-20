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

namespace GenerallyPositive
{
    public class VkCodeToDomKeyConverter
    {
        // Dictionary mapping VK codes to DOM key and code values
        private static readonly Dictionary<byte, (string Key, string Code)> VkToDomMap = new Dictionary<byte, (string, string)>
    {
        // Printable keys (letters, digits, symbols)
        { 0x30, ("0", "Digit0") }, // VK_0
        { 0x31, ("1", "Digit1") }, // VK_1
        { 0x32, ("2", "Digit2") }, // VK_2
        { 0x33, ("3", "Digit3") }, // VK_3
        { 0x34, ("4", "Digit4") }, // VK_4
        { 0x35, ("5", "Digit5") }, // VK_5
        { 0x36, ("6", "Digit6") }, // VK_6
        { 0x37, ("7", "Digit7") }, // VK_7
        { 0x38, ("8", "Digit8") }, // VK_8
        { 0x39, ("9", "Digit9") }, // VK_9
        { 0x41, ("a", "KeyA") }, // VK_A
        { 0x42, ("b", "KeyB") }, // VK_B
        { 0x43, ("c", "KeyC") }, // VK_C
        { 0x44, ("d", "KeyD") }, // VK_D
        { 0x45, ("e", "KeyE") }, // VK_E
        { 0x46, ("f", "KeyF") }, // VK_F
        { 0x47, ("g", "KeyG") }, // VK_G
        { 0x48, ("h", "KeyH") }, // VK_H
        { 0x49, ("i", "KeyI") }, // VK_I
        { 0x4A, ("j", "KeyJ") }, // VK_J
        { 0x4B, ("k", "KeyK") }, // VK_K
        { 0x4C, ("l", "KeyL") }, // VK_L
        { 0x4D, ("m", "KeyM") }, // VK_M
        { 0x4E, ("n", "KeyN") }, // VK_N
        { 0x4F, ("o", "KeyO") }, // VK_O
        { 0x50, ("p", "KeyP") }, // VK_P
        { 0x51, ("q", "KeyQ") }, // VK_Q
        { 0x52, ("r", "KeyR") }, // VK_R
        { 0x53, ("s", "KeyS") }, // VK_S
        { 0x54, ("t", "KeyT") }, // VK_T
        { 0x55, ("u", "KeyU") }, // VK_U
        { 0x56, ("v", "KeyV") }, // VK_V
        { 0x57, ("w", "KeyW") }, // VK_W
        { 0x58, ("x", "KeyX") }, // VK_X
        { 0x59, ("y", "KeyY") }, // VK_Y
        { 0x5A, ("z", "KeyZ") }, // VK_Z
        { 0xBA, (";", "Semicolon") }, // VK_OEM_1
        { 0xBB, ("=", "Equal") }, // VK_OEM_PLUS
        { 0xBC, (",", "Comma") }, // VK_OEM_COMMA
        { 0xBD, ("-", "Minus") }, // VK_OEM_MINUS
        { 0xBE, (".", "Period") }, // VK_OEM_PERIOD
        { 0xBF, ("/", "Slash") }, // VK_OEM_2
        { 0xC0, ("`", "Backquote") }, // VK_OEM_妹
        { 0xDB, ("[", "BracketLeft") }, // VK_OEM_4
        { 0xDC, ("\\", "Backslash") }, // VK_OEM_5
        { 0xDD, ("]", "BracketRight") }, // VK_OEM_6
        { 0xDE, ("'", "Quote") }, // VK_OEM_7

        // Number pad keys
        { 0x60, ("0", "Numpad0") }, // VK_NUMPAD0
        { 0x61, ("1", "Numpad1") }, // VK_NUMPAD1
        { 0x62, ("2", "Numpad2") }, // VK_NUMPAD2
        { 0x63, ("3", "Numpad3") }, // VK_NUMPAD3
        { 0x64, ("4", "Numpad4") }, // VK_NUMPAD4
        { 0x65, ("5", "Numpad5") }, // VK_NUMPAD5
        { 0x66, ("6", "Numpad6") }, // VK_NUMPAD6
        { 0x67, ("7", "Numpad7") }, // VK_NUMPAD7
        { 0x68, ("8", "Numpad8") }, // VK_NUMPAD8
        { 0x69, ("9", "Numpad9") }, // VK_NUMPAD9
        { 0x6A, ("*", "NumpadMultiply") }, // VK_MULTIPLY
        { 0x6B, ("+", "NumpadAdd") }, // VK_ADD
        { 0x6D, ("-", "NumpadSubtract") }, // VK_SUBTRACT
        { 0x6E, (".", "NumpadDecimal") }, // VK_DECIMAL
        { 0x6F, ("/", "NumpadDivide") }, // VK_DIVIDE

        // Function keys
        { 0x70, ("F1", "F1") }, // VK_F1
        { 0x71, ("F2", "F2") }, // VK_F2
        { 0x72, ("F3", "F3") }, // VK_F3
        { 0x73, ("F4", "F4") }, // VK_F4
        { 0x74, ("F5", "F5") }, // VK_F5
        { 0x75, ("F6", "F6") }, // VK_F6
        { 0x76, ("F7", "F7") }, // VK_F7
        { 0x77, ("F8", "F8") }, // VK_F8
        { 0x78, ("F9", "F9") }, // VK_F9
        { 0x79, ("F10", "F10") }, // VK_F10
        { 0x7A, ("F11", "F11") }, // VK_F11
        { 0x7B, ("F12", "F12") }, // VK_F12

        // Navigation and special keys
        { 0x08, ("Backspace", "Backspace") }, // VK_BACK
        { 0x09, ("Tab", "Tab") }, // VK_TAB
        { 0x0D, ("Enter", "Enter") }, // VK_RETURN
        { 0x1B, ("Escape", "Escape") }, // VK_ESCAPE
        { 0x20, (" ", "Space") }, // VK_SPACE
        { 0x21, ("PageUp", "PageUp") }, // VK_PRIOR
        { 0x22, ("PageDown", "PageDown") }, // VK_NEXT
        { 0x23, ("End", "End") }, // VK_END
        { 0x24, ("Home", "Home") }, // VK_HOME
        { 0x25, ("ArrowLeft", "ArrowLeft") }, // VK_LEFT
        { 0x26, ("ArrowUp", "ArrowUp") }, // VK_UP
        { 0x27, ("ArrowRight", "ArrowRight") }, // VK_RIGHT
        { 0x28, ("ArrowDown", "ArrowDown") }, // VK_DOWN
        { 0x2C, ("PrintScreen", "PrintScreen") }, // VK_SNAPSHOT
        { 0x2D, ("Insert", "Insert") }, // VK_INSERT
        { 0x2E, ("Delete", "Delete") }, // VK_DELETE
        { 0x5B, ("Meta", "MetaLeft") }, // VK_LWIN
        { 0x5C, ("Meta", "MetaRight") }, // VK_RWIN
    };

        /// <summary>
        /// Converts a Windows VK code to DOM KeyboardEvent key and code values.
        /// </summary>
        /// <param name="vkCode">The Windows Virtual Key Code (e.g., 0x24 for VK_HOME).</param>
        /// <param name="key">Output: The DOM KeyboardEvent.key value (e.g., "Home").</param>
        /// <param name="code">Output: The DOM KeyboardEvent.code value (e.g., "Home").</param>
        /// <returns>True if the VK code was found and converted; false otherwise.</returns>
        public static bool TryConvertVkCodeToDomKey(byte vkCode, out string key, out string code)
        {
            if (VkToDomMap.TryGetValue(vkCode, out var result))
            {
                key = result.Key;
                code = result.Code;
                return true;
            }

            // Handle printable ASCII characters dynamically (e.g., VK_A = 0x41)
            if (vkCode >= 0x30 && vkCode <= 0x39) // Digits 0-9
            {
                key = ((char)vkCode).ToString();
                code = $"Digit{key}";
                return true;
            }
            if (vkCode >= 0x41 && vkCode <= 0x5A) // Letters A-Z
            {
                key = ((char)(vkCode + 32)).ToString(); // Convert to lowercase (e.g., 'a')
                code = $"Key{char.ToUpper((char)vkCode)}"; // e.g., "KeyA"
                return true;
            }

            // Fallback for unmapped keys
            key = null;
            code = null;
            return false;
        }

        /// <summary>
        /// Converts an ASCII character to a Windows Virtual Key (VK) code.
        /// </summary>
        /// <param name="asciiChar">The ASCII character to convert (e.g., 'a', '0', ';').</param>
        /// <param name="vkCode">Output: The Windows Virtual Key Code (e.g., 0x41 for 'a').</param>
        /// <returns>True if the ASCII character was found and converted; false otherwise.</returns>
        public static bool TryConvertAsciiToVkCode(char asciiChar, out byte vkCode)
        {
            // Initialize output
            vkCode = 0;

            // Validate ASCII range (0-127)
            if (asciiChar > 127)
            {
                return false;
            }

            // Handle letters (case-insensitive: 'a' or 'A' both map to VK_A)
            if (char.IsLetter(asciiChar) && asciiChar >= 'A' && asciiChar <= 'Z' || asciiChar >= 'a' && asciiChar <= 'z')
            {
                vkCode = (byte)char.ToUpper(asciiChar); // VK_A to VK_Z (0x41 to 0x5A)
                return true;
            }

            // Handle digits
            if (char.IsDigit(asciiChar))
            {
                vkCode = (byte)asciiChar; // VK_0 to VK_9 (0x30 to 0x39)
                return true;
            }

            // Search VkToDomMap for other printable ASCII characters (e.g., ';', '=', ',')
            string keyStr = asciiChar.ToString();
            var match = VkToDomMap.FirstOrDefault(kvp => kvp.Value.Key == keyStr);
            if (match.Key != 0)
            {
                vkCode = match.Key;
                return true;
            }

            // Handle space explicitly
            if (asciiChar == ' ')
            {
                vkCode = 0x20; // VK_SPACE
                return true;
            }

            // Return false for unmapped characters
            return false;
        }
    }
}

