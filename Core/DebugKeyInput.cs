using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AvatarReplacer
{
    internal static class DebugKeyInput
    {
        [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private static readonly Dictionary<KeyCode, bool> PreviousHeldState = new Dictionary<KeyCode, bool>();
        private static bool rawInputUnavailable;

        // Unity and user32, then detect the transition ourselves.
        public static bool WasPressed(KeyCode key)
        {
            bool held = GetKey(key);
            PreviousHeldState.TryGetValue(key, out bool wasHeld);
            PreviousHeldState[key] = held;
            return held && !wasHeld;
        }

        public static bool GetKey(KeyCode key)
        {
            bool unityDown = false;
            try { unityDown = Input.GetKey(key); }
            catch (Exception) { }

            return unityDown || GetRawKey(key);
        }

        public static float GetMouseWheelDelta()
        {
            try
            {
                float delta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(delta) > 0.001f) return delta;
            }
            catch (Exception) { }

            try { return Input.GetAxisRaw("Mouse ScrollWheel"); }
            catch (Exception) { return 0f; }
        }

        private static bool GetRawKey(KeyCode key)
        {
            return TryGetVirtualKey(key, out int virtualKey) && GetRawKey(virtualKey);
        }

        private static bool GetRawKey(int virtualKey)
        {
            if (rawInputUnavailable) return false;
            try { return (GetAsyncKeyState(virtualKey) & 0x8000) != 0; }
            catch (DllNotFoundException) { rawInputUnavailable = true; }
            catch (EntryPointNotFoundException) { rawInputUnavailable = true; }
            catch (BadImageFormatException) { rawInputUnavailable = true; }
            return false;
        }

        private static bool TryGetVirtualKey(KeyCode key, out int virtualKey)
        {
            string name = key.ToString();

            if (name.Length == 1 && name[0] >= 'a' && name[0] <= 'z')
            {
                virtualKey = char.ToUpperInvariant(name[0]);
                return true;
            }

            if (name.StartsWith("Alpha", StringComparison.Ordinal) &&
                int.TryParse(name.Substring(5), out int digit) && digit >= 0 && digit <= 9)
            {
                virtualKey = 0x30 + digit;
                return true;
            }

            if (name.StartsWith("Keypad", StringComparison.Ordinal) &&
                int.TryParse(name.Substring(6), out digit) && digit >= 0 && digit <= 9)
            {
                virtualKey = 0x60 + digit;
                return true;
            }

            if (name.Length > 1 && name[0] == 'F' &&
                int.TryParse(name.Substring(1), out int functionKey) && functionKey >= 1 && functionKey <= 24)
            {
                virtualKey = 0x70 + functionKey - 1;
                return true;
            }

            switch (name)
            {
                case "Backspace": virtualKey = 0x08; return true;
                case "Tab": virtualKey = 0x09; return true;
                case "Clear": virtualKey = 0x0C; return true;
                case "Return":
                case "KeypadEnter": virtualKey = 0x0D; return true;
                case "Shift": virtualKey = 0x10; return true;
                case "LeftShift": virtualKey = 0xA0; return true;
                case "RightShift": virtualKey = 0xA1; return true;
                case "Control": virtualKey = 0x11; return true;
                case "LeftControl": virtualKey = 0xA2; return true;
                case "RightControl": virtualKey = 0xA3; return true;
                case "Alt": virtualKey = 0x12; return true;
                case "LeftAlt": virtualKey = 0xA4; return true;
                case "RightAlt": virtualKey = 0xA5; return true;
                case "AltGr": virtualKey = 0xA5; return true;
                case "Pause": virtualKey = 0x13; return true;
                case "CapsLock": virtualKey = 0x14; return true;
                case "Escape": virtualKey = 0x1B; return true;
                case "Space": virtualKey = 0x20; return true;
                case "PageUp": virtualKey = 0x21; return true;
                case "PageDown": virtualKey = 0x22; return true;
                case "End": virtualKey = 0x23; return true;
                case "Home": virtualKey = 0x24; return true;
                case "LeftArrow": virtualKey = 0x25; return true;
                case "UpArrow": virtualKey = 0x26; return true;
                case "RightArrow": virtualKey = 0x27; return true;
                case "DownArrow": virtualKey = 0x28; return true;
                case "Insert": virtualKey = 0x2D; return true;
                case "Delete": virtualKey = 0x2E; return true;
                case "Numlock": virtualKey = 0x90; return true;
                case "ScrollLock": virtualKey = 0x91; return true;
                case "KeypadMultiply": virtualKey = 0x6A; return true;
                case "KeypadPlus": virtualKey = 0x6B; return true;
                case "KeypadMinus": virtualKey = 0x6D; return true;
                case "KeypadPeriod": virtualKey = 0x6E; return true;
                case "KeypadDivide": virtualKey = 0x6F; return true;
                case "Semicolon": virtualKey = 0xBA; return true;
                case "Equals": virtualKey = 0xBB; return true;
                case "Comma": virtualKey = 0xBC; return true;
                case "Minus": virtualKey = 0xBD; return true;
                case "Period": virtualKey = 0xBE; return true;
                case "Slash": virtualKey = 0xBF; return true;
                case "BackQuote": virtualKey = 0xC0; return true;
                case "LeftBracket": virtualKey = 0xDB; return true;
                case "Backslash": virtualKey = 0xDC; return true;
                case "RightBracket": virtualKey = 0xDD; return true;
                case "Quote": virtualKey = 0xDE; return true;
                default:
                    virtualKey = 0;
                    return false;
            }
        }
    }
}
