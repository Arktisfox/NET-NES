using ImGuiNET;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public class GUI_Remapper
    {
        public bool Showing = false;

        private EmulatorButton? rebindingButton = null; // which button (if any) is waiting to capture an input
        private enum RebindMode { Primary, Alt, Gamepad }
        private RebindMode rebindingMode = RebindMode.Primary;
        private Input input;

        public void Draw()
        {
            // If we're capturing an input for a rebind, intercept the next key/button pressed
            // (rather than letting it fall through to gameplay/menu shortcuts) and consume it
            // as the new binding. Escape cancels (for key capture; gamepad capture also honors it).
            if (rebindingButton.HasValue)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    rebindingButton = null;
                }
                else if (rebindingMode == RebindMode.Gamepad)
                {
                    if (Raylib.IsGamepadAvailable(input.GamepadIndex))
                    {
                        foreach (GamepadButton pad in Enum.GetValues(typeof(GamepadButton)))
                        {
                            if (pad == GamepadButton.Unknown) continue;
                            if (Raylib.IsGamepadButtonPressed(input.GamepadIndex, pad))
                            {
                                input.GamepadBindings[rebindingButton.Value] = pad;
                                // input.SaveBindings();
                                rebindingButton = null;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    foreach (KeyboardKey key in Enum.GetValues(typeof(KeyboardKey)))
                    {
                        if (key == KeyboardKey.Null) continue;
                        if (Raylib.IsKeyPressed(key))
                        {
                            if (rebindingMode == RebindMode.Alt)
                            {
                                input.AltBindings[rebindingButton.Value] = key;
                            }
                            else
                            {
                                input.Bindings[rebindingButton.Value] = key;
                            }
                            // input.SaveBindings();
                            rebindingButton = null;
                            break;
                        }
                    }
                }
            }

            var imScreenSize = ImGui.GetIO().DisplaySize;
            ImGui.SetNextWindowPos(new Vector2(imScreenSize.X / 2.0f, imScreenSize.Y / 2.0f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(340 * 2, 400), ImGuiCond.Appearing);
            if (ImGui.Begin("Controls", ref Showing))
            {
                ImGui.TextWrapped("Click a button, then press the key/gamepad button you want to bind. Escape cancels.");
                ImGui.Spacing();
                ImGui.Separator();

                // --- Gamepad picker ---
                var availablePads = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    if (Raylib.IsGamepadAvailable(i)) availablePads.Add(i);
                }

                if (availablePads.Count == 0)
                {
                    ImGui.TextColored(new Vector4(1, 0.6f, 0.2f, 1), "No gamepad detected.");
                    ImGui.TextWrapped("Plug in a controller (an Xbox/XInput pad works out of the box) - it'll be picked up automatically.");
                }
                else
                {
                    string current = "Gamepad " + input.GamepadIndex;

                    if (ImGui.BeginCombo("Gamepad", current))
                    {
                        foreach (int i in availablePads)
                        {
                            bool selected = i == input.GamepadIndex;
                            if (ImGui.Selectable("Gamepad " + i + "##pad" + i, selected))
                            {
                                input.GamepadIndex = i;
                                // input.SaveBindings();
                            }
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    bool useStick = input.UseLeftStickAsDPad;
                    if (ImGui.Checkbox("Use left stick as D-Pad", ref useStick))
                    {
                        input.UseLeftStickAsDPad = useStick;
                        // input.SaveBindings();
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();

                ImGui.Columns(4, "controlsColumns", false);
                ImGui.Text("Button"); ImGui.NextColumn();
                ImGui.Text("Primary"); ImGui.NextColumn();
                ImGui.Text("Alternate"); ImGui.NextColumn();
                ImGui.Text("Gamepad"); ImGui.NextColumn();
                ImGui.Separator();

                foreach (EmulatorButton button in Enum.GetValues(typeof(EmulatorButton)))
                {
                    // Numbered save-state slots (0-9) are meant to stay on their
                    // number-row defaults - exposing 20 near-identical rows here
                    // just adds noise. Quick Save/Load remain remappable above.
                    if (button is >= EmulatorButton.LoadState0 and <= EmulatorButton.LoadState9) continue;
                    if (button is >= EmulatorButton.SaveState0 and <= EmulatorButton.SaveState9) continue;

                    ImGui.Text(button.ToString());
                    ImGui.NextColumn();

                    bool capturingPrimary = rebindingButton == button && rebindingMode == RebindMode.Primary;
                    string primaryLabel = capturingPrimary ? "Press a key..." : input.Bindings[button].ToString();
                    if (ImGui.Button(primaryLabel + "##primary_" + button))
                    {
                        rebindingButton = button;
                        rebindingMode = RebindMode.Primary;
                    }
                    ImGui.NextColumn();

                    bool capturingAlt = rebindingButton == button && rebindingMode == RebindMode.Alt;
                    var alt = input.AltBindings[button];
                    string altLabel = capturingAlt ? "Press a key..." : (alt.HasValue ? alt.Value.ToString() : "(none)");
                    if (ImGui.Button(altLabel + "##alt_" + button))
                    {
                        rebindingButton = button;
                        rebindingMode = RebindMode.Alt;
                    }
                    ImGui.SameLine();
                    if (alt.HasValue && ImGui.SmallButton("x##clearalt_" + button))
                    {
                        input.AltBindings[button] = null;
                        // input.SaveBindings();
                    }
                    ImGui.NextColumn();

                    bool capturingPad = rebindingButton == button && rebindingMode == RebindMode.Gamepad;
                    var pad = input.GamepadBindings[button];
                    string padLabel = capturingPad ? "Press a button..." : (pad.HasValue ? pad.Value.ToString() : "(none)");
                    if (ImGui.Button(padLabel + "##pad_" + button))
                    {
                        rebindingButton = button;
                        rebindingMode = RebindMode.Gamepad;
                    }
                    ImGui.SameLine();
                    if (pad.HasValue && ImGui.SmallButton("x##clearpad_" + button))
                    {
                        input.GamepadBindings[button] = null;
                        // input.SaveBindings();
                    }
                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Reset to Defaults"))
                {
                    input.ResetToDefaults();
                    // input.SaveBindings();
                    rebindingButton = null;
                }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    Showing = false;
                    rebindingButton = null;
                }
            }
            ImGui.End();
        }

        public GUI_Remapper(Input input)
        {
            this.input = input;
        }
    }
}