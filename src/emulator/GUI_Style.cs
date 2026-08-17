using ImGuiNET;
using System.Numerics;

namespace Emulator
{
    public class GUI_Style
    {
        public static void ApplyNesTheme()
        {
            ImGuiStylePtr style = ImGui.GetStyle();

            // Sharp 8-bit pixel corners
            style.WindowRounding = 0.0f;
            style.ChildRounding = 0.0f;
            style.FrameRounding = 0.0f;
            style.GrabRounding = 0.0f;
            style.PopupRounding = 0.0f;
            style.ScrollbarRounding = 0.0f;
            style.TabRounding = 0.0f;
            style.WindowBorderSize = 2.0f;
            style.FrameBorderSize = 1.0f;

            // NES Hardware Color Mapping via ImGui.NET Indexer
            style.Colors[(int)ImGuiCol.Text] = new Vector4(1.00f, 1.00f, 1.00f, 1.00f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.65f, 0.65f, 0.65f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);

            // NES Controller Red Accents
            Vector4 nesRed = new Vector4(0.75f, 0.00f, 0.00f, 1.00f);
            Vector4 nesRedHover = new Vector4(0.90f, 0.10f, 0.10f, 1.00f);
            Vector4 nesRedActive = new Vector4(1.00f, 0.20f, 0.20f, 1.00f);

            style.Colors[(int)ImGuiCol.Button] = nesRed;
            style.Colors[(int)ImGuiCol.ButtonHovered] = nesRedHover;
            style.Colors[(int)ImGuiCol.ButtonActive] = nesRedActive;

            style.Colors[(int)ImGuiCol.Header] = nesRed;
            style.Colors[(int)ImGuiCol.HeaderHovered] = nesRedHover;
            style.Colors[(int)ImGuiCol.HeaderActive] = nesRedActive;

            style.Colors[(int)ImGuiCol.CheckMark] = nesRedHover;
            style.Colors[(int)ImGuiCol.SliderGrab] = nesRed;
        }
    }
}