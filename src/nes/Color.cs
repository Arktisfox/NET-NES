namespace NES
{
    public struct Color
    {
        public byte R, G, B;

        public Color() {  R = 0; G = 0; B = 0; }
        public Color(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }
}
