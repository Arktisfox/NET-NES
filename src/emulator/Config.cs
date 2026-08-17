using Newtonsoft.Json.Linq;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public class Config
    {
        // Audio
        public double Volume = 1.0;

        // Video
        public int Scale = 2;
        public TvSystemMode TvSystemMode = TvSystemMode.Auto;

        // Debug
        public bool DrawFPS = false;

        // Input
        public Dictionary<EmulatorButton, KeyboardKey> Bindings = Input.DefaultBindings();
        public Dictionary<EmulatorButton, KeyboardKey?> AltBindings = Input.DefaultAltBindings();

        public Dictionary<EmulatorButton, GamepadButton?> GamepadBindings = Input.DefaultGamepadBindings();
        public int GamepadIndex = 0;

        public bool UseLeftStickAsDPad = true;
    }
}