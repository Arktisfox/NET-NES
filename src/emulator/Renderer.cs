using NES;
using Raylib_cs;

namespace Emulator
{
    public class Renderer
    {
        private int paddingTop = 0;
        private int paddingBottom = 0;
        private int paddingLeft = 0;
        private int paddingRight = 0;

        public int PaddingTop
        {
            get => paddingTop;
            set => SetPadding(ref paddingTop, value, PPU.ScreenHeight, paddingBottom);
        }
        public int PaddingBottom
        {
            get => paddingBottom;
            set => SetPadding(ref paddingBottom, value, PPU.ScreenHeight, paddingTop);
        }
        public int PaddingLeft
        {
            get => paddingLeft;
            set => SetPadding(ref paddingLeft, value, PPU.ScreenWidth, paddingRight);
        }
        public int PaddingRight
        {
            get => paddingRight;
            set => SetPadding(ref paddingRight, value, PPU.ScreenWidth, paddingLeft);
        }

        private void SetPadding(ref int field, int value, int axisLength, int opposite)
        {
            // Leave at least 1 pixel visible on the axis
            int maxAllowed = axisLength - opposite - 1;
            int clamped = Math.Max(0, Math.Min(value, maxAllowed));
            field = clamped;
        }


        private int textureX = 0;
        private int textureY = 0;

        private Image image;
        private Texture2D texture;

        private int RenderWidth => PPU.ScreenWidth - paddingLeft - paddingRight;
        private int RenderHeight => PPU.ScreenHeight - paddingTop - paddingBottom;

        public void DrawFrame(PPU ppu, int scale)
        {
            var frameBuffer = ppu.FrameBuffer;
            Raylib_cs.Color color = Raylib_cs.Color.Black;
            Raylib_cs.Color black = Raylib_cs.Color.Black;

            int startX = paddingLeft;
            int endX = PPU.ScreenWidth - paddingRight;
            int startY = paddingTop;
            int endY = PPU.ScreenHeight - paddingBottom;

            for (int y = 0; y < PPU.ScreenHeight; y++)
            {
                bool rowPadded = y < startY || y >= endY;
                for (int x = 0; x < PPU.ScreenWidth; x++)
                {
                    if (rowPadded || x < startX || x >= endX)
                    {
                        Raylib.ImageDrawPixel(ref image, x, y, black);
                        continue;
                    }
                    var fbColor = frameBuffer[y * PPU.ScreenWidth + x];
                    color.R = fbColor.R;
                    color.G = fbColor.G;
                    color.B = fbColor.B;
                    Raylib.ImageDrawPixel(ref image, x, y, color);
                }
            }
            unsafe
            {
                Raylib.UpdateTexture(texture, image.Data);
            }
            Raylib.DrawTextureEx(texture, new System.Numerics.Vector2(textureX, textureY),
                                 0.0f, scale, Raylib_cs.Color.White);
        }

        public Renderer()
        {
            var screenWidth = PPU.ScreenWidth;
            var screenHeight = PPU.ScreenHeight;

            image = Raylib.GenImageColor(screenWidth, screenHeight, Raylib_cs.Color.Black);
            texture = Raylib.LoadTextureFromImage(image);
        }
    }
}
