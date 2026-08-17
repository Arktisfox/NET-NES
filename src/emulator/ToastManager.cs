using ImGuiNET;
using System.Numerics;

namespace Emulator
{
    public static class ToastManager
    {
        private class Toast
        {
            public string Message = "";
            public DateTime ExpiresAt;
        }

        private const double DisplaySeconds = 2.5;
        private const double FadeSeconds = 0.4;
        private const int MaxVisible = 5;

        private const float Padding = 10f;
        private const float LineGap = 6f;

        private static readonly List<Toast> active = new();

        public static void Show(string message)
        {
            active.Add(new Toast
            {
                Message = message,
                ExpiresAt = DateTime.Now.AddSeconds(DisplaySeconds)
            });

            while (active.Count > MaxVisible)
                active.RemoveAt(0);
        }

        public static void Draw()
        {
            var now = DateTime.Now;
            active.RemoveAll(t => now >= t.ExpiresAt);

            if (active.Count == 0) return;

            var drawList = ImGui.GetForegroundDrawList();
            var io = ImGui.GetIO();

            // Stack newest message at the bottom, older ones above it.
            float y = io.DisplaySize.Y - Padding;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                Toast toast = active[i];

                double remaining = (toast.ExpiresAt - now).TotalSeconds;
                float alpha = remaining < FadeSeconds
                    ? (float)Math.Clamp(remaining / FadeSeconds, 0.0, 1.0)
                    : 1f;

                Vector2 textSize = ImGui.CalcTextSize(toast.Message);
                Vector2 boxSize = textSize + new Vector2(16, 10);

                float top = y - boxSize.Y;
                Vector2 boxMin = new Vector2(Padding, top);
                Vector2 boxMax = boxMin + boxSize;

                uint bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.08f, 0.75f * alpha));
                uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0.65f, 0.65f, 0.65f, 0.9f * alpha));
                uint text = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));

                drawList.AddRectFilled(boxMin, boxMax, bg);
                drawList.AddRect(boxMin, boxMax, border);
                drawList.AddText(boxMin + new Vector2(8, 5), text, toast.Message);

                y = top - LineGap;
            }
        }
    }
}