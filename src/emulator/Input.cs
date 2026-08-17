using Raylib_cs;

namespace Emulator
{
    public enum EmulatorButton
    {
        A = 0,
        B = 1,
        Select = 2,
        Start = 3,
        Up = 4,
        Down = 5,
        Left = 6,
        Right = 7,

        QuickLoadState = 8,
        QuickSaveState = 9,

        LoadState0 = 10,
        LoadState1 = 11,
        LoadState2 = 12,
        LoadState3 = 13,
        LoadState4 = 14,
        LoadState5 = 15,
        LoadState6 = 16,
        LoadState7 = 17,
        LoadState8 = 18,
        LoadState9 = 19,

        SaveState0 = 20,
        SaveState1 = 21,
        SaveState2 = 22,
        SaveState3 = 23,
        SaveState4 = 24,
        SaveState5 = 25,
        SaveState6 = 26,
        SaveState7 = 27,
        SaveState8 = 28,
        SaveState9 = 29,
    }

    public class Input
    {
        // bindings
        public Dictionary<EmulatorButton, bool> RequireCtrl = DefaultRequireCtrl();
        public Dictionary<EmulatorButton, KeyboardKey> Bindings = DefaultBindings();
        public Dictionary<EmulatorButton, KeyboardKey?> AltBindings = DefaultAltBindings();

        public Dictionary<EmulatorButton, GamepadButton?> GamepadBindings = DefaultGamepadBindings();
        public int GamepadIndex = 0;

        public bool UseLeftStickAsDPad = true;
        private const float StickDeadzone = 0.5f;

        public static Dictionary<EmulatorButton, KeyboardKey> DefaultBindings()
        {
            var bindings = new Dictionary<EmulatorButton, KeyboardKey>
        {
            { EmulatorButton.A, KeyboardKey.X },
            { EmulatorButton.B, KeyboardKey.Z },
            { EmulatorButton.Select, KeyboardKey.RightShift },
            { EmulatorButton.Start, KeyboardKey.Enter },
            { EmulatorButton.Up, KeyboardKey.Up },
            { EmulatorButton.Down, KeyboardKey.Down },
            { EmulatorButton.Left, KeyboardKey.Left },
            { EmulatorButton.Right, KeyboardKey.Right },

            { EmulatorButton.QuickSaveState, KeyboardKey.Q },
            { EmulatorButton.QuickLoadState, KeyboardKey.W },
        };

            // 1-9, then 0 for slot 9 - matches the physical top-row key order.
            KeyboardKey[] numberRow = {

            KeyboardKey.Zero, KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, 
            KeyboardKey.Five, KeyboardKey.Six, KeyboardKey.Seven, KeyboardKey.Eight, KeyboardKey.Nine,
        };

            for (int slot = 0; slot < 10; slot++)
            {
                bindings[(EmulatorButton)((int)EmulatorButton.LoadState0 + slot)] = numberRow[slot];
                bindings[(EmulatorButton)((int)EmulatorButton.SaveState0 + slot)] = numberRow[slot];
            }

            return bindings;
        }

        public static Dictionary<EmulatorButton, KeyboardKey?> DefaultAltBindings()
        {
            var alt = new Dictionary<EmulatorButton, KeyboardKey?>();
            foreach (EmulatorButton button in Enum.GetValues(typeof(EmulatorButton)))
            {
                alt[button] = null;
            }
            return alt;
        }

        public static Dictionary<EmulatorButton, GamepadButton?> DefaultGamepadBindings()
        {
            // Standard Xbox-style layout: A=RightFaceDown, B=RightFaceLeft,
            // Back/View=MiddleLeft, Start/Menu=MiddleRight, D-Pad=LeftFace*.
            // State slots have no default gamepad binding - remap manually if wanted.
            var pad = new Dictionary<EmulatorButton, GamepadButton?>();
            foreach (EmulatorButton button in Enum.GetValues(typeof(EmulatorButton)))
            {
                pad[button] = null;
            }

            pad[EmulatorButton.A] = GamepadButton.RightFaceDown;
            pad[EmulatorButton.B] = GamepadButton.RightFaceLeft;
            pad[EmulatorButton.Select] = GamepadButton.MiddleLeft;
            pad[EmulatorButton.Start] = GamepadButton.MiddleRight;
            pad[EmulatorButton.Up] = GamepadButton.LeftFaceUp;
            pad[EmulatorButton.Down] = GamepadButton.LeftFaceDown;
            pad[EmulatorButton.Left] = GamepadButton.LeftFaceLeft;
            pad[EmulatorButton.Right] = GamepadButton.LeftFaceRight;

            return pad;
        }

        public static Dictionary<EmulatorButton, bool> DefaultRequireCtrl()
        {
            var req = new Dictionary<EmulatorButton, bool>();
            foreach (EmulatorButton button in Enum.GetValues(typeof(EmulatorButton)))
            {
                req[button] = false;
            }

            for (int slot = 0; slot < 10; slot++)
            {
                req[(EmulatorButton)((int)EmulatorButton.SaveState0 + slot)] = true;
            }

            return req;
        }

        // state
        public byte ControllerState => controllerState;
        private byte controllerState = 0;

        // implementation
        public void ResetToDefaults()
        {
            Bindings = DefaultBindings();
            GamepadBindings = DefaultGamepadBindings();
            foreach (var button in Bindings.Keys)
            {
                AltBindings[button] = null;
            }
        }

        private bool IsButtonDown(EmulatorButton button)
        {
            if (Raylib.IsKeyDown(Bindings[button])) return true;

            var alt = AltBindings[button];
            if (alt.HasValue && Raylib.IsKeyDown(alt.Value)) return true;

            if (Raylib.IsGamepadAvailable(GamepadIndex))
            {
                var pad = GamepadBindings[button];
                if (pad.HasValue && Raylib.IsGamepadButtonDown(GamepadIndex, pad.Value)) return true;

                if (UseLeftStickAsDPad)
                {
                    float x = Raylib.GetGamepadAxisMovement(GamepadIndex, GamepadAxis.LeftX);
                    float y = Raylib.GetGamepadAxisMovement(GamepadIndex, GamepadAxis.LeftY);

                    switch (button)
                    {
                        case EmulatorButton.Left: if (x < -StickDeadzone) return true; break;
                        case EmulatorButton.Right: if (x > StickDeadzone) return true; break;
                        case EmulatorButton.Up: if (y < -StickDeadzone) return true; break;
                        case EmulatorButton.Down: if (y > StickDeadzone) return true; break;
                    }
                }
            }

            return false;
        }

        private bool CtrlHeld()
        {
            return Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        }

        // For one-shot actions (save/load state, quick save/load) rather than held
        // gameplay buttons. Checks the bound key was just pressed this frame AND
        // that Ctrl is held/not-held exactly as this button requires - so "1" and
        // "Ctrl+1" bound to the same physical key never both fire on one keypress.
        public bool IsHotkeyPressed(EmulatorButton button)
        {
            bool needsCtrl = RequireCtrl.TryGetValue(button, out bool req) && req;
            if (CtrlHeld() != needsCtrl) return false;

            if (Raylib.IsKeyPressed(Bindings[button])) return true;

            var alt = AltBindings[button];
            if (alt.HasValue && Raylib.IsKeyPressed(alt.Value)) return true;

            return false;
        }

        public void UpdateController()
        {
            controllerState = 0;
            if (IsButtonDown(EmulatorButton.A)) controllerState |= 1 << 0;
            if (IsButtonDown(EmulatorButton.B)) controllerState |= 1 << 1;
            if (IsButtonDown(EmulatorButton.Select)) controllerState |= 1 << 2;
            if (IsButtonDown(EmulatorButton.Start)) controllerState |= 1 << 3;
            if (IsButtonDown(EmulatorButton.Up)) controllerState |= 1 << 4;
            if (IsButtonDown(EmulatorButton.Down)) controllerState |= 1 << 5;
            if (IsButtonDown(EmulatorButton.Left)) controllerState |= 1 << 6;
            if (IsButtonDown(EmulatorButton.Right)) controllerState |= 1 << 7;
        }
    }
}