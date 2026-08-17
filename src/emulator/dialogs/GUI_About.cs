using ImGuiNET;

namespace Emulator
{
    public class GUI_About
    {
        public bool Showing = false;

        public void Draw()
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 20), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(125 * 2, 122 * 2), ImGuiCond.Appearing);
            if (ImGui.Begin("About", ref Showing, flags))
            {
                ImGui.Text("NET-NES");
                ImGui.Text(Helper.version.ToString());
                ImGui.Text("Made by Bot Randomness :)");
                ImGui.Text("Updated by Arktisfox");

                ImGui.Text(" ____________________________ ");
                ImGui.Text("| |  NES               |---| |");
                ImGui.Text("| |____________________|___| |");
                ImGui.Text("|____________________________|");
                ImGui.Text("|                     1  2   |");
                ImGui.Text(" \\ O [ ] [ ]          D  D  / ");
                ImGui.Text("  *------------------------*  ");

                if (ImGui.Button("Close"))
                {
                    Showing = false;
                }
            }
            ImGui.End();
        }
    }
}