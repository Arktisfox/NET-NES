using ImGuiNET;

namespace Emulator
{
    public class GUI_VideoSettings
    {
        public bool Showing = false;

        private Renderer renderer;

        public void Draw()
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 20), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(125 * 3, 50 * 4), ImGuiCond.Appearing);
            if (ImGui.Begin("Video Settings", ref Showing, flags))
            {
                int paddingTop = renderer.PaddingTop;
                if(ImGui.SliderInt("Top Padding", ref paddingTop, 0, NES.PPU.ScreenHeight / 2))
                {
                    renderer.PaddingTop = paddingTop;
                }

                int paddingBottom = renderer.PaddingBottom;
                if (ImGui.SliderInt("Bottom Padding", ref paddingBottom, 0, NES.PPU.ScreenHeight / 2))
                {
                    renderer.PaddingBottom = paddingBottom;
                }

                int paddingLeft = renderer.PaddingLeft;
                if (ImGui.SliderInt("Left Padding", ref paddingLeft, 0, NES.PPU.ScreenWidth / 2))
                {
                    renderer.PaddingLeft = paddingLeft;
                }

                int paddingRight = renderer.PaddingRight;
                if (ImGui.SliderInt("Right Padding", ref paddingRight, 0, NES.PPU.ScreenWidth / 2))
                {
                    renderer.PaddingRight = paddingRight;
                }

                ImGui.Spacing();
                ImGui.SliderInt("Scale", ref EmulatorState.scale, 1, 10);
                

                if (ImGui.Button("Close"))
                {
                    Showing = false;
                }
            }
            ImGui.End();
        }

        public GUI_VideoSettings(Renderer renderer)
        {
            this.renderer = renderer;
        }
    }
}