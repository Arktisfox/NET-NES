using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public class GUI_Scale
    {
        public bool Showing = false;

        public void Draw()
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 20), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(125 * 2, 50 * 2), ImGuiCond.Appearing);
            if (ImGui.Begin("Window Size Config", ref Showing, flags))
            {
                ImGui.SliderInt("Scale", ref EmulatorState.scale, 1, 10);
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