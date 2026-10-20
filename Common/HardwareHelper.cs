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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using static GenerallyPositive.Enums;
using ModifierKeys = GenerallyPositive.Enums.ModifierKeys;

namespace GenerallyPositive
{
    /// <summary>
    /// Low-level Windows input simulation (HardwareEngine/HardwareHW) - mouse movement and clicks via mouse_event, and keyboard input via keybd_event - used as an alternative to WebDriver/CDP-level interaction.
    /// </summary>
    internal class HardwareHelper
    {
        // virtual keycodes: https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes?redirectedfrom=MSDN
        // scan codes: https://en.wikipedia.org/wiki/Scancode
        static byte scanCode = 0;

        // One shared Random; a new fixed-seed Random per call would return the same values.
        private static readonly Random _random = new Random();

        /// <summary>
        /// Computes a randomized inter-character typing delay from a ceiling value. A constant, identical pause
        /// between every character is itself a detectable signature, so GPAL.WithTypingDelay's value is treated
        /// as a ceiling rather than a fixed pace: returns 0 if ceilingMs is 0 or less (no delay), otherwise a
        /// random value between Math.Min(25, ceilingMs) and ceilingMs, inclusive.
        /// </summary>
        /// <param name="ceilingMs">Upper bound (inclusive) for the randomized delay, as set via GPAL.WithTypingDelay.</param>
        internal static int GetTypingDelay(int ceilingMs)
        {
            if (0 >= ceilingMs)
                return 0;

            int floorMs = Math.Min(25, ceilingMs);
            return _random.Next(floorMs, ceilingMs + 1);
        }

        /// <summary>
        /// Translates a character to its virtual keycode and required shift state for the current keyboard layout.
        /// </summary>
        /// <param name="ch">Character to translate.</param>
        /// <returns>Low byte is the virtual keycode; bit 0x100 of the high byte indicates the Shift key is required.</returns>
        // https://stackoverflow.com/questions/26845532/c-sharp-ascii-to-keycode
        [DllImport("user32.dll")]
        public static extern short VkKeyScan(char ch);

        /// <summary>
        /// Simulates a keyboard key press or release event.
        /// </summary>
        /// <param name="bVk">Virtual keycode of the key.</param>
        /// <param name="bScan">Hardware scan code of the key.</param>
        /// <param name="dwFlags">Combination of KEYEVENTF flags (e.g. extended key, key up).</param>
        /// <param name="dwExtraInfo">Additional event-specific value.</param>
        // http://tksinghal.blogspot.com/2011/04/how-to-press-and-hold-keyboard-key.html
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        /// <summary>
        /// Moves the mouse cursor to the given screen coordinates.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        /// <returns>True if the cursor was moved successfully.</returns>
        // https://docs.microsoft.com/en-us/dotnet/desktop/winforms/input-mouse/how-to-simulate-events?view=netdesktop-6.0
        [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        /// <summary>
        /// Retrieves the current mouse cursor position in screen coordinates.
        /// </summary>
        /// <param name="p">Receives the current cursor position.</param>
        /// <returns>True if the position was retrieved successfully.</returns>
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point p);

        /// <summary>
        /// How long Windows allows between two presses for them to count as a double click, in milliseconds.
        /// The user can change this, so it is asked for rather than assumed.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        /// <summary>
        /// Whether a key is down right now. The high bit is set while the key is held.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;
        private const int SM_CXDRAG = 68;
        private const int SM_CYDRAG = 69;

        // Time and position of the last button press, to keep the next press from becoming a double click.
        private static DateTime _lastPress = DateTime.MinValue;
        private static Point _lastPressPoint = new Point(int.MinValue, int.MinValue);

        /// <summary>
        /// Records a button press, so the next one can tell whether it would be read as the second half of a
        /// double click.
        /// </summary>
        /// <param name="x">X coordinate the press was made at, in screen pixels.</param>
        /// <param name="y">Y coordinate the press was made at, in screen pixels.</param>
        private static void RecordPress(int x, int y)
        {
            _lastPress = DateTime.UtcNow;
            _lastPressPoint = new Point(x, y);
        }

        /// <summary>
        /// If this press is within the double-click time and rectangle of the last one, waits out the double-click
        /// time.
        /// </summary>
        /// <param name="x">X coordinate the press is about to be made at, in screen pixels.</param>
        /// <param name="y">Y coordinate the press is about to be made at, in screen pixels.</param>
        private static void SeparateFromLastPress(int x, int y)
        {
            if (int.MinValue != _lastPressPoint.X)
            {
                int window = (int)GetDoubleClickTime();
                int elapsed = (int)(DateTime.UtcNow - _lastPress).TotalMilliseconds;

                bool nearby = Math.Abs(x - _lastPressPoint.X) <= GetSystemMetrics(SM_CXDOUBLECLK) / 2
                           && Math.Abs(y - _lastPressPoint.Y) <= GetSystemMetrics(SM_CYDOUBLECLK) / 2;

                if (true == nearby && elapsed < window)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Holding [{window - elapsed}]ms so this press is not read as a double click with the one before it.", null, GPALObjectType.None);
                    Thread.Sleep(window - elapsed + 20);
                }
            }
        }

        /// <summary>
        /// Simulates a mouse event (button down/up, movement) at the given coordinates.
        /// </summary>
        /// <param name="dwFlags">Combination of MouseEventFlags values describing the event.</param>
        /// <param name="dx">X coordinate (or motion delta) for the event.</param>
        /// <param name="dy">Y coordinate (or motion delta) for the event.</param>
        /// <param name="dwData">Additional data for the event (unused for click/move).</param>
        /// <param name="dwExtraInfo">Additional event-specific value.</param>
        // https://stackoverflow.com/questions/8739523/directing-mouse-events-dllimportuser32-dll-click-double-click
        [DllImport("user32.dll")]
        static extern void mouse_event(int dwFlags, int dx, int dy,
                              int dwData, int dwExtraInfo);

        /// <summary>
        /// Flags passed to mouse_event identifying which mouse button/action to simulate, and whether coordinates are absolute.
        /// </summary>
        [Flags]
        public enum MouseEventFlags
        {
            LEFTDOWN = 0x00000002,
            LEFTUP = 0x00000004,
            MIDDLEDOWN = 0x00000020,
            MIDDLEUP = 0x00000040,
            MOVE = 0x00000001,
            ABSOLUTE = 0x00008000,
            RIGHTDOWN = 0x00000008,
            RIGHTUP = 0x00000010
        }

        /// <summary>
        /// Releases any modifier key that is down but not in <paramref name="wanted"/>.
        /// </summary>
        /// <remarks>
        /// The shell reads modifiers at the drop, so a stray Control turns a drag into a copy.
        /// </remarks>
        /// <param name="wanted">The modifiers this operation needs held. All others are released.</param>
        internal static void ReleaseStrayModifiers(ModifierKeys wanted)
        {
            foreach (var held in new[]
            {
                (Key: ModifierKeys.Control, Left: GPAL.VK_CONTROL_LEFT, Right: GPAL.VK_CONTROL_RIGHT),
                (Key: ModifierKeys.Shift, Left: GPAL.VK_SHIFT_LEFT, Right: GPAL.VK_SHIFT_RIGHT),
                (Key: ModifierKeys.Alt, Left: GPAL.VK_LMENU, Right: GPAL.VK_RMENU),
            })
            {
                if (0 != (held.Key & wanted))
                    continue;

                if (0 != (GetAsyncKeyState(held.Left) & 0x8000) || 0 != (GetAsyncKeyState(held.Right) & 0x8000))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{held.Key}] was still held and nothing asked for it, so it is being released before this action.", null, GPALObjectType.None);

                    ReleaseModifierKeys(held.Key, true);
                }
            }
        }

        /// <summary>
        /// Moves the pointer from the grab point to the drop point with a button held, past Windows' drag threshold.
        /// </summary>
        /// <remarks>
        /// Windows starts a drag only after it sees the pointer pass the drag threshold, so the non-simulated path
        /// steps past it before jumping to the drop.
        /// </remarks>
        /// <param name="toX">X coordinate to release at, in screen pixels.</param>
        /// <param name="toY">Y coordinate to release at, in screen pixels.</param>
        /// <param name="simulate">True for a human-like path, false to step past the threshold and jump to the
        /// drop.</param>
        internal static void DragTravel(int toX, int toY, bool simulate)
        {
            if (true == simulate)
                MoveMouse(toX, toY, 4, 4, 0.5);
            else
            {
                GetCursorPos(out Point from);

                int stepX = from.X + Math.Sign(toX - from.X) * (GetSystemMetrics(SM_CXDRAG) + 2);
                int stepY = from.Y + Math.Sign(toY - from.Y) * (GetSystemMetrics(SM_CYDRAG) + 2);

                MoveMouse(stepX, stepY);
                Thread.Sleep(30);
                MoveMouse(toX, toY);
                Thread.Sleep(30);
            }
        }

        /// <summary>
        /// Moves the mouse to the given coordinates and presses (without releasing) the left or right mouse button.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="clickType">ClickType.LeftClick, ClickType.MiddleClick or ClickType.RightClick. Any other value publishes an ERROR event.</param>
        public static void MouseDown(int x, int y, ClickType clickType)
        {
            SeparateFromLastPress(x, y);
            RecordPress(x, y);

            MoveMouse(x, y);
            if (ClickType.LeftClick == clickType)
                mouse_event((int)(MouseEventFlags.LEFTDOWN), x, y, 0, 0);
            else if (ClickType.MiddleClick == clickType)
                mouse_event((int)(MouseEventFlags.MIDDLEDOWN), x, y, 0, 0);
            else if (ClickType.RightClick == clickType)
                mouse_event((int)(MouseEventFlags.RIGHTDOWN), x, y, 0, 0);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unknown mouse down clicktype [{clickType}]", null, GPALObjectType.None);
        }
        /// <summary>
        /// Moves the mouse to the given coordinates and releases a previously-pressed left or right mouse button.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="clickType">ClickType.LeftClick or ClickType.RightClick. Any other value publishes an ERROR event.</param>
        public static void MouseUp(int x, int y, ClickType clickType)
        {
            MoveMouse(x, y);
            if (ClickType.LeftClick == clickType)
                mouse_event((int)(MouseEventFlags.LEFTUP), x, y, 0, 0);
            else if (ClickType.MiddleClick == clickType)
                mouse_event((int)(MouseEventFlags.MIDDLEUP), x, y, 0, 0);
            else if (ClickType.RightClick == clickType)
                mouse_event((int)(MouseEventFlags.RIGHTUP), x, y, 0, 0);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unknown mouse down clicktype [{clickType}]", null, GPALObjectType.None);
        }

        /// <summary>
        /// Moves the mouse to the given coordinates and performs a middle-button click (down and up).
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        public static void MiddleClick(int x, int y)
        {
            MoveMouse(x, y);
            // Separate down + up with a short dwell - a zero-duration combined click is dropped
            // intermittently by browsers (notably Firefox). Mirrors the reliable RightClick pattern.
            mouse_event((int)(MouseEventFlags.MIDDLEDOWN), x, y, 0, 0);
            Thread.Sleep(10);
            mouse_event((int)(MouseEventFlags.MIDDLEUP), x, y, 0, 0);

            RecordPress(x, y);
        }
        /// <summary>
        /// Moves the mouse to the given coordinates and performs a left-button click (down and up).
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        public static void LeftClick(int x, int y)
        {
            MoveMouse(x, y);
            // Separate down + up with a short dwell - a zero-duration combined click is dropped
            // intermittently by browsers (notably Firefox). Mirrors the reliable RightClick pattern.
            mouse_event((int)(MouseEventFlags.LEFTDOWN), x, y, 0, 0);
            Thread.Sleep(10);
            mouse_event((int)(MouseEventFlags.LEFTUP), x, y, 0, 0);

            RecordPress(x, y);
        }
        /// <summary>
        /// Moves the mouse to the given coordinates and performs two left-button clicks separated by a short delay.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        public static void LeftDoubleClick(int x, int y)
        {
            MoveMouse(x, y);
            mouse_event((int)(MouseEventFlags.LEFTDOWN | MouseEventFlags.LEFTUP), x, y, 0, 0);
            Thread.Sleep(15);
            mouse_event((int)(MouseEventFlags.LEFTDOWN | MouseEventFlags.LEFTUP), x, y, 0, 0);

            RecordPress(x, y);
        }
        /// <summary>
        /// Moves the mouse to the given coordinates and performs a right-button click, holding the button briefly between down and up.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        public static void RightClick(int x, int y)
        {
            MoveMouse(x, y);
            // Simulate right mouse button down
            mouse_event((int)MouseEventFlags.RIGHTDOWN, x, y, 0, 0);
            Thread.Sleep(150);
            // Simulate right mouse button up
            mouse_event((int)MouseEventFlags.RIGHTUP, x, y, 0, 0);

            RecordPress(x, y);
        }
        /// <summary>
        /// Instantly moves the mouse cursor to the given coordinates, clamping negative values to 0.
        /// </summary>
        /// <param name="x">Target X coordinate in screen pixels.</param>
        /// <param name="y">Target Y coordinate in screen pixels.</param>
        public static void MoveMouse(int x, int y)
        {
            x = 0 <= x ? x : 0;
            y = 0 <= y ? y : 0;

            SetCursorPos(x, y);
        }
        /// <summary>
        /// Presses (without releasing) the keyboard keys for the given modifier combination, e.g. both Control keys, both Shift keys, Alt, Windows, the Application/Menu key, or Scroll Lock.
        /// </summary>
        /// <param name="modifierKeys">Bitwise combination of modifier keys to press.</param>
        /// <param name="dontPubolish">When true, suppresses the INFO event normally published for this action.</param>
        internal static void PressModifierKey(ModifierKeys modifierKeys, bool dontPubolish = false)
        {
            if (false == dontPubolish)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Pressed [{Browser.Browser.GetModifierKeys(modifierKeys)}]", modifierKeys, GPALObjectType.Other);

            // The extended flag belongs to the key. Right Control and Right Alt are extended; the left ones and both
            // Shifts are not. A mismatched flag on keyup leaves the key stuck down.
            if (0 != (ModifierKeys.Control & modifierKeys))
            {
                keybd_event(GPAL.VK_CONTROL_LEFT, 0, 0, 0);
                keybd_event(GPAL.VK_CONTROL_RIGHT, 0, GPAL.KEYEVENTF_EXTENDEDKEY, 0);
            }

            if (0 != (ModifierKeys.Shift & modifierKeys))
            {
                keybd_event(GPAL.VK_SHIFT_LEFT, 0, 0, 0);
                keybd_event(GPAL.VK_SHIFT_RIGHT, 0, 0, 0);
            }

            if (0 != (ModifierKeys.Alt & modifierKeys))
            {
                keybd_event(GPAL.VK_LMENU, 0, 0, 0);                              // Left Alt
                keybd_event(GPAL.VK_RMENU, 0, GPAL.KEYEVENTF_EXTENDEDKEY, 0);     // Right Alt
            }

            if (0 != (ModifierKeys.Windows & modifierKeys))
            {
                keybd_event(GPAL.VK_LWIN, 0, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
                keybd_event(GPAL.VK_RWIN, 0, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
            }

            if (0 != (ModifierKeys.Application & modifierKeys))
            {
                keybd_event(GPAL.VK_APPS, 0, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
            }

            if (0 != (ModifierKeys.ScrollLock & modifierKeys))
            {
                keybd_event(GPAL.VK_SCROLL, 0, 0, 0); // Press Scroll Lock, which is not an extended key
            }
        }

        /// <summary>
        /// Releases the keyboard keys for the given modifier combination, in reverse order of PressModifierKey, with short delays between each release.
        /// </summary>
        /// <param name="modifierKeys">Bitwise combination of modifier keys to release.</param>
        /// <param name="dontPublish">When true, suppresses the INFO event normally published for this action.</param>
        internal static void ReleaseModifierKeys(ModifierKeys modifierKeys, bool dontPublish = false)
        {
            if (false == dontPublish)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Released [{Browser.Browser.GetModifierKeys(modifierKeys)}]", modifierKeys, GPALObjectType.Other);

            // Release each key with the extended flag it was pressed with.
            if (0 != (ModifierKeys.Alt & modifierKeys))
            {
                keybd_event(GPAL.VK_RMENU, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0); // Right Alt
                Thread.Sleep(150);
                keybd_event(GPAL.VK_LMENU, 0, GPAL.KEYEVENTF_KEYUP, 0);                              // Left Alt
            }

            if (0 != (ModifierKeys.Control & modifierKeys))
            {
                keybd_event(GPAL.VK_CONTROL_RIGHT, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
                Thread.Sleep(150);
                keybd_event(GPAL.VK_CONTROL_LEFT, 0, GPAL.KEYEVENTF_KEYUP, 0);
            }

            if (0 != (ModifierKeys.Shift & modifierKeys))
            {
                keybd_event(GPAL.VK_SHIFT_RIGHT, 0, GPAL.KEYEVENTF_KEYUP, 0);
                Thread.Sleep(150);
                keybd_event(GPAL.VK_SHIFT_LEFT, 0, GPAL.KEYEVENTF_KEYUP, 0);
            }

            if (0 != (ModifierKeys.Windows & modifierKeys))
            {
                keybd_event(GPAL.VK_RWIN, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
                Thread.Sleep(150);
                keybd_event(GPAL.VK_LWIN, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
            }

            if (0 != (ModifierKeys.Application & modifierKeys))
            {
                keybd_event(GPAL.VK_APPS, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
            }

            if (0 != (ModifierKeys.ScrollLock & modifierKeys))
            {
                keybd_event(GPAL.VK_SCROLL, 0, GPAL.KEYEVENTF_KEYUP, 0); // Release Scroll Lock
            }
        }
        /// <summary>
        /// Presses and releases a single virtual-key code via keybd_event, optionally holding modifier keys for the duration.
        /// </summary>
        /// <param name="keyToSend">Virtual keycode to send.</param>
        /// <param name="modifierKeys">Modifier keys to press before and release after sending the key.</param>
        /// <returns>Always true.</returns>
        public static bool SendKey(byte keyToSend, ModifierKeys modifierKeys = ModifierKeys.NONE)

        {
            if (ModifierKeys.NONE != modifierKeys)
                PressModifierKey(modifierKeys);

            try
            {
                keybd_event(keyToSend, scanCode, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
                Thread.Sleep(_random.Next(10, 30)); // dwell time between press down and release, milliseconds
                keybd_event(keyToSend, scanCode, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
            }
            catch { }

            if (ModifierKeys.NONE != modifierKeys)
                ReleaseModifierKeys(modifierKeys);

            return true;
        }
        /// <summary>
        /// Sends each character of a string as a hardware key event via SendChar.
        /// </summary>
        /// <param name="stringToSend">Text to type.</param>
        /// <param name="delayMs">Ceiling for the inter-character pacing delay in milliseconds. 0 sends every character back-to-back; greater than 0 sleeps a random duration between Math.Min(25, delayMs) and delayMs between characters.</param>
        /// <remarks>
        /// With Shift held, mixed case text is not sent all caps.
        /// </remarks>
        // CAVEAT: if SHIFT modifier is used with a mixed case string, sendchar will unshift and the whole string will not be sent in all caps.
        public static void SendString(string stringToSend, int delayMs = 0)
        {
            bool first = true;
            foreach (char c in stringToSend)
            {
                if (false == first && 0 < delayMs)
                    Thread.Sleep(GetTypingDelay(delayMs));
                SendChar(c, ModifierKeys.NONE, true);
                first = false;
            }
        }
        /// <summary>
        /// Sends a single character as a hardware key event, looking up the required virtual keycode and Shift state via VkKeyScan, and optionally holding additional modifier keys for the duration.
        /// </summary>
        /// <param name="charToSend">Character to type.</param>
        /// <param name="modifierKeys">Additional modifier keys to press before and release after sending the character.</param>
        /// <param name="dontPublish">When true, suppresses the INFO events normally published when pressing/releasing modifierKeys.</param>
        public static void SendChar(char charToSend, ModifierKeys modifierKeys = ModifierKeys.NONE, bool dontPublish = false)
        {
            short vkKeyScan = HardwareHelper.VkKeyScan(charToSend);
            System.Windows.Forms.Keys vkCode = (System.Windows.Forms.Keys)(vkKeyScan & 0xff);

            var shift = (vkKeyScan & 0x100) > 0;
            //var ctrl = (vkKeyScan & 0x200) > 0;
            //var alt = (vkKeyScan & 0x400) > 0;

            if (true == shift)  // uppercase char - keybd event has no concept of uppercase 'S', that is shift + 's' even if ascii has uppercase, keyboards do not
            {
                keybd_event(GPAL.VK_SHIFT_LEFT, 0, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
            }

            if (ModifierKeys.NONE != modifierKeys)
                PressModifierKey(modifierKeys, dontPublish);

            try
            {
                keybd_event((byte)vkCode, (byte)vkKeyScan, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
                Thread.Sleep(_random.Next(10, 30)); // dwell time between press down and release, milliseconds
                keybd_event((byte)vkCode, (byte)vkKeyScan, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
            }
            catch { }

            if (ModifierKeys.NONE != modifierKeys)
                ReleaseModifierKeys(modifierKeys, dontPublish);

            if (true == shift)
            {
                keybd_event(GPAL.VK_SHIFT_LEFT, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
            }

        }

        /// <summary>
        /// Dispatches to LeftClick, LeftDoubleClick, MiddleClick, or RightClick based on the given ClickType.
        /// </summary>
        /// <param name="x">X coordinate in screen pixels.</param>
        /// <param name="y">Y coordinate in screen pixels.</param>
        /// <param name="clickType">Type of click to perform. Unrecognized values are silently ignored.</param>
        public static void HardwareClick(int x, int y, ClickType clickType)
        {
            switch (clickType)
            {
                case ClickType.LeftClick:
                    LeftClick(x, y);
                    break;
                case ClickType.LeftDoubleClick:
                    LeftDoubleClick(x, y);
                    break;
                case ClickType.MiddleClick:
                    MiddleClick(x, y);
                    break;
                case ClickType.RightClick:
                    RightClick(x, y);
                    break;
            }

        }

        // https://stackoverflow.com/questions/913646/c-sharp-moving-the-mouse-around-realistically
        // https://ben.land/post/2021/04/25/windmouse-human-mouse-movement/
        #region Improved WindMouse - Less Bounce, More Natural (C# 8 Compatible)

        /// <summary>
        /// Moves the mouse to the target coordinates along a human-like curved path (WindMouse algorithm), with small random jitter on the target and configurable speed.
        /// </summary>
        /// <param name="targetX">Target X coordinate in screen pixels.</param>
        /// <param name="targetY">Target Y coordinate in screen pixels.</param>
        /// <param name="randomnessX">Maximum random offset applied to targetX, in pixels.</param>
        /// <param name="randomnessY">Maximum random offset applied to targetY, in pixels.</param>
        /// <param name="speed">Speed multiplier; higher values move faster with larger steps and shorter waits. Values below 0.4 are clamped to 0.4.</param>
        public static void MoveMouse(int targetX, int targetY, int randomnessX = 4, int randomnessY = 4, double speed = 1.0)
        {
            Point current = new Point();
            GetCursorPos(out current);

            Random rnd = new Random();

            // Small target randomness
            targetX = Math.Max(0, targetX + rnd.Next(-randomnessX, randomnessX + 1));
            targetY = Math.Max(0, targetY + rnd.Next(-randomnessY, randomnessY + 1));

            double adjustedSpeed = Math.Max(0.4, speed);

            ImprovedWindMouse(
                current.X, current.Y,
                targetX, targetY,
                gravity: 9.0,
                wind: 2.5,
                minWait: 7.0 / adjustedSpeed,
                maxWait: 16.0 / adjustedSpeed,
                maxStep: 38.0 * adjustedSpeed,
                targetArea: 18.0
            );
        }

        /// <summary>
        /// Moves the cursor from (xs, ys) to (xe, ye) step-by-step using a wind/gravity simulation that produces curved, variable-speed mouse movement, then settles precisely onto the target.
        /// </summary>
        /// <param name="xs">Starting X coordinate.</param>
        /// <param name="ys">Starting Y coordinate.</param>
        /// <param name="xe">Target X coordinate.</param>
        /// <param name="ye">Target Y coordinate.</param>
        /// <param name="gravity">Strength of the pull toward the target.</param>
        /// <param name="wind">Strength of random lateral drift while far from the target.</param>
        /// <param name="minWait">Minimum delay in milliseconds between movement steps.</param>
        /// <param name="maxWait">Maximum delay in milliseconds between movement steps.</param>
        /// <param name="maxStep">Maximum distance moved per step, in pixels.</param>
        /// <param name="targetArea">Distance from the target at which movement begins to slow and damp.</param>
        private static void ImprovedWindMouse(double xs, double ys, double xe, double ye,
            double gravity, double wind, double minWait, double maxWait,
            double maxStep, double targetArea)
        {
            double dist, windX = 0, windY = 0, veloX = 0, veloY = 0;
            int oldX, oldY, newX = (int)Math.Round(xs), newY = (int)Math.Round(ys);

            double waitDiff = maxWait - minWait;
            double sqrt2 = Math.Sqrt(2.0);
            double sqrt3 = Math.Sqrt(3.0);
            double sqrt5 = Math.Sqrt(5.0);

            Random rnd = new Random();

            dist = Hypot(xe - xs, ye - ys);

            while (dist > 0.8)
            {
                wind = Math.Min(wind, dist * 0.8);

                if (dist >= targetArea)
                {
                    // Far from target
                    windX = windX / sqrt3 + (rnd.NextDouble() * wind * 2 - wind) / sqrt5;
                    windY = windY / sqrt3 + (rnd.NextDouble() * wind * 2 - wind) / sqrt5;
                }
                else
                {
                    // Close to target - smooth damping
                    windX = windX / sqrt2 * 0.75;
                    windY = windY / sqrt2 * 0.75;

                    if (maxStep < 4)
                        maxStep = 3.0 + rnd.NextDouble() * 2;
                    else
                        maxStep = maxStep / sqrt5 * 0.85;
                }

                // Gravity pull (weaker when very close)
                double pull = gravity * (dist > 1.0 ? 1.0 : dist);
                veloX += windX + pull * (xe - xs) / dist;
                veloY += windY + pull * (ye - ys) / dist;

                // Limit velocity
                double mag = Hypot(veloX, veloY);
                if (mag > maxStep)
                {
                    double stepSize = maxStep * 0.55 + rnd.NextDouble() * (maxStep * 0.45);
                    veloX = (veloX / mag) * stepSize;
                    veloY = (veloY / mag) * stepSize;
                }

                // Extra damping near target
                if (dist < targetArea * 0.6)
                {
                    veloX *= 0.88;
                    veloY *= 0.88;
                }

                oldX = (int)Math.Round(xs);
                oldY = (int)Math.Round(ys);
                xs += veloX;
                ys += veloY;
                dist = Hypot(xe - xs, ye - ys);

                newX = (int)Math.Round(xs);
                newY = (int)Math.Round(ys);

                if (oldX != newX || oldY != newY)
                    SetCursorPos(newX, newY);

                double step = Hypot(xs - oldX, ys - oldY);
                int wait = (int)Math.Round(waitDiff * (step / maxStep) + minWait);
                Thread.Sleep(Math.Max(1, wait));
            }

            // Gentle final settling (no hard snap)
            int finalX = (int)Math.Round(xe);
            int finalY = (int)Math.Round(ye);

            if (Math.Abs(newX - finalX) > 1 || Math.Abs(newY - finalY) > 1)
            {
                while (Math.Abs(newX - finalX) > 0 || Math.Abs(newY - finalY) > 0)
                {
                    int dx = Math.Sign(finalX - newX) * rnd.Next(1, 3);
                    int dy = Math.Sign(finalY - newY) * rnd.Next(1, 3);

                    newX = Math.Max(finalX - 3, Math.Min(finalX + 3, newX + dx));
                    newY = Math.Max(finalY - 3, Math.Min(finalY + 3, newY + dy));

                    SetCursorPos(newX, newY);
                    Thread.Sleep(rnd.Next(8, 18));
                }
            }

            SetCursorPos(finalX, finalY);
        }

        /// <summary>
        /// Computes the Euclidean distance between two points given as (dx, dy) deltas.
        /// </summary>
        /// <param name="dx">Difference in X coordinates.</param>
        /// <param name="dy">Difference in Y coordinates.</param>
        /// <returns>The straight-line distance sqrt(dx^2 + dy^2).</returns>
        private static double Hypot(double dx, double dy)
        {
            return Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
        #region VKCODE to ASCII
        /// <summary>
        /// Helpers for converting Windows virtual-key codes to their ASCII character representation under the current keyboard state.
        /// </summary>
        internal static class KeyboardUtils
        {
            /// <summary>
            /// Translates a virtual-key code and scan code into the corresponding character(s) for the given keyboard state.
            /// </summary>
            /// <param name="uVirtKey">Virtual-key code to translate.</param>
            /// <param name="uScanCode">Hardware scan code corresponding to uVirtKey.</param>
            /// <param name="lpKeyState">256-byte array representing the state of each virtual key.</param>
            /// <param name="lpChar">Receives the translated character(s).</param>
            /// <param name="uFlags">Menu-active flag bit; 0 for normal translation.</param>
            /// <returns>0 if no translation, 1 if one character was written, 2 if a dead-key composite was written, or negative if a dead key was buffered.</returns>
            // P/Invoke declarations
            [DllImport("user32.dll")]
            private static extern int ToAscii(uint uVirtKey, uint uScanCode, byte[] lpKeyState,
                [Out] StringBuilder lpChar, uint uFlags);

            /// <summary>
            /// Retrieves the current state of all 256 virtual keys.
            /// </summary>
            /// <param name="lpKeyState">256-byte array to receive the key state.</param>
            /// <returns>Nonzero on success, zero on failure.</returns>
            [DllImport("user32.dll")]
            private static extern int GetKeyboardState(byte[] lpKeyState);

            /// <summary>
            /// Translates a virtual-key code into a scan code, character value, or another virtual-key code, depending on uMapType.
            /// </summary>
            /// <param name="uCode">Virtual-key code or scan code to translate.</param>
            /// <param name="uMapType">Translation to perform (0 = virtual-key to scan code).</param>
            /// <returns>The translated value, or 0 if there is no translation.</returns>
            [DllImport("user32.dll")]
            private static extern uint MapVirtualKey(uint uCode, uint uMapType);

            /// <summary>
            /// Converts a virtual keycode to an ASCII character based on current keyboard state.
            /// </summary>
            /// <param name="vkCode">Virtual keycode (e.g., 0x41 for VK_A)</param>
            /// <returns>ASCII character as a string, or empty string if no ASCII mapping exists</returns>
            internal static string VkCodeToAscii(uint vkCode)
            {
                // Get the scan code for the virtual key
                uint scanCode = MapVirtualKey(vkCode, 0); // MAPVK_VK_TO_VSC

                // Get current keyboard state
                byte[] keyState = new byte[256];
                if (GetKeyboardState(keyState) == 0)
                    return string.Empty;

                // Adjust keyboard state based on your application's maintained state
                // Example: If Shift is "held" in your state, set VK_SHIFT (0x10)
                // keyState[0x10] = 0x80; // Uncomment to simulate Shift pressed
                // keyState[0x14] = 0x80; // Uncomment to simulate Caps Lock toggled

                // Buffer for output character
                StringBuilder charBuffer = new StringBuilder(2);

                // Call ToAscii to translate VK code to ASCII
                int result = ToAscii(vkCode, scanCode, keyState, charBuffer, 0);

                if (result == 1)
                {
                    // One character was written to the buffer
                    return charBuffer[0].ToString();
                }
                else if (result == 2)
                {
                    // Two characters (e.g., dead key combo); return first
                    return charBuffer[0].ToString();
                }
                else
                {
                    // No translation (e.g., VK_SCROLL, VK_LMENU) or error
                    return string.Empty;
                }
            }
        }
        #endregion VKCODE to ASCII
    }
}

