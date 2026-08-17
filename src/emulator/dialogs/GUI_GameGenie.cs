using ImGuiNET;
using System.Numerics;
using NES;
using GameGenie;

namespace Emulator
{
    public class GUI_GameGenie
    {
        public bool Showing = false;

        private string inputBuffer = "";
        private string descriptionBuffer = "";

        private NES.NES? nes => EmulatorState.NES;

        public void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(20, 40), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new Vector2(460, 320), ImGuiCond.Appearing);

            if (ImGui.Begin("Game Genie Codes", ref Showing))
            {
                if (nes == null)
                {
                    ImGui.TextColored(new Vector4(1, 0.6f, 0.2f, 1), "Load a ROM first.");
                    ImGui.End();
                    return;
                }

                var cart = nes.Cartridge;

                ImGui.TextWrapped("Enter a 6- or 8-letter Game Genie code (e.g. SXIOPO). " +
                    "Codes apply immediately and are cleared when a new ROM is loaded.");
                ImGui.Spacing();

                // Code
                ImGui.SetNextItemWidth(160);
                bool enterPressed = ImGui.InputText(
                    "Code",
                    ref inputBuffer,
                    9,
                    ImGuiInputTextFlags.CharsUppercase |
                    ImGuiInputTextFlags.EnterReturnsTrue);

                // Description
                ImGui.SetNextItemWidth(300);
                ImGui.InputText("Description", ref descriptionBuffer, 128);

                ImGui.SameLine();
                bool addClicked = ImGui.Button("Add");

                if (addClicked || enterPressed)
                {
                    string entered = inputBuffer.Trim().ToUpperInvariant();
                    string description = descriptionBuffer.Trim();

                    if (CheatManager.AddCode(
                        cart,
                        entered,
                        string.IsNullOrEmpty(description) ? null : description))
                    {
                        ToastManager.Show($"Game Genie code added: {entered}");

                        inputBuffer = "";
                        descriptionBuffer = "";
                    }
                    else
                    {
                        ToastManager.Show($"Invalid Game Genie code: {entered}");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var codes = CheatManager.GetCodesForGame(cart);

                if (codes.Count == 0)
                {
                    ImGui.TextDisabled("No active codes.");
                }
                else
                {
                    string? codeToRemove = null;

                    foreach (var code in codes)
                    {
                        // Enabled checkbox
                        bool enabled = code.Enabled;

                        if (ImGui.Checkbox(
                            $"##enabled_{code.Code}",
                            ref enabled))
                        {
                            CheatManager.SetEnabled(code.Code, enabled);
                        }

                        ImGui.SameLine();

                        // Description
                        if (!string.IsNullOrEmpty(code.Description))
                        {
                            if (!code.Enabled)
                                ImGui.TextDisabled($"{code.Description}:");
                            else
                                ImGui.Text($"{code.Description}:");

                            ImGui.SameLine();
                        }

                        // Code
                        if (!code.Enabled)
                            ImGui.TextDisabled(code.Code);
                        else
                            ImGui.Text(code.Code);

                        ImGui.SameLine();

                        if (GameGenie.GameGenie.TryParse(code.Code, out var genie))
                        {
                            string detail = genie.Compare.HasValue
                                ? $"(${genie.Address:X4}: {genie.Data:X2} if ${genie.Compare.Value:X2})"
                                : $"(${genie.Address:X4}: {genie.Data:X2})";

                            ImGui.TextDisabled(detail);
                        }
                        else
                        {
                            ImGui.TextDisabled("Invalid Code");
                        }

                        ImGui.SameLine();

                        if (ImGui.SmallButton("Remove##" + code.Code))
                        {
                            codeToRemove = code.Code;
                        }
                    }

                    if (codeToRemove != null)
                    {
                        CheatManager.RemoveCode(cart, codeToRemove);
                        ToastManager.Show($"Game Genie code removed: {codeToRemove}");
                    }

                    ImGui.Spacing();

                    if (ImGui.Button("Clear All"))
                    {
                        CheatManager.ClearCodes(cart);
                        ToastManager.Show("Game Genie codes cleared");
                    }
                }
            }

            ImGui.End();
        }
    }
}