using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public class GUI_Manual
    {
        public bool Showing = false;

        public void Draw()
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 20), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(155 * 2, 120 * 2), ImGuiCond.Appearing);
            if (ImGui.Begin("Manual", ref Showing, flags))
            {
                ImGui.Text("Press [SPACE] to toggle the menu bar.");
                ImGui.Spacing();
                ImGui.Text("Press 0-9 to load states, CTRL+0-9 to save.");
                ImGui.Spacing();
                ImGui.Text("In Debug: Toggle Sprite0 Hit Check. \nTry this out if a game freezes");
                ImGui.Spacing();
                ImGui.Text("Only Catridge Mapper types Supported:");
                ImGui.Text("MMC1, MMC3, NROM, UxROM");
                ImGui.Spacing();

                if (ImGui.Button("Close"))
                {
                    Showing = false;
                }
            }
            ImGui.End();
        }
    }
}
