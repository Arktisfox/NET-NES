namespace GameGenie
{
    /// <summary>
    /// A single decoded Game Genie code.
    /// </summary>
    public readonly struct GenieCode
    {
        /// <summary>Normalized (uppercase, trimmed) code text, e.g. "SXIOPO".</summary>
        public readonly string Code;

        /// <summary>CPU address ($8000-$FFFF) this code overrides.</summary>
        public readonly ushort Address;

        /// <summary>Value to return instead of what the cartridge would supply.</summary>
        public readonly byte Data;

        /// <summary>
        /// For 8-letter codes only: the substitution only applies if the
        /// cartridge's original byte at Address equals this. 6-letter codes
        /// have no compare and always apply.
        /// </summary>
        public readonly byte? Compare;

        public GenieCode(string code, ushort address, byte data, byte? compare)
        {
            Code = code;
            Address = address;
            Data = data;
            Compare = compare;
        }
    }

    /// <summary>
    /// Decodes classic NES Game Genie codes (6-letter, unconditional; 8-letter,
    /// conditional on an original byte match). See
    /// https://nesdoug.com/2020/06/16/game-genie/ or any Game Genie code FAQ for
    /// background on the format.
    /// </summary>
    public static class GameGenie
    {
        private static readonly Dictionary<char, int> Letters = new()
        {
            ['A'] = 0x0,
            ['P'] = 0x1,
            ['Z'] = 0x2,
            ['L'] = 0x3,
            ['G'] = 0x4,
            ['I'] = 0x5,
            ['T'] = 0x6,
            ['Y'] = 0x7,
            ['E'] = 0x8,
            ['O'] = 0x9,
            ['X'] = 0xA,
            ['U'] = 0xB,
            ['K'] = 0xC,
            ['S'] = 0xD,
            ['V'] = 0xE,
            ['N'] = 0xF,
        };

        public static bool TryParse(string rawCode, out GenieCode result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(rawCode)) return false;

            string code = rawCode.Trim().ToUpperInvariant();
            if (code.Length != 6 && code.Length != 8) return false;

            int[] n = new int[code.Length];
            for (int i = 0; i < code.Length; i++)
            {
                if (!Letters.TryGetValue(code[i], out n[i])) return false;
            }

            int address = 0x8000
                | ((n[3] & 0x7) << 12)
                | ((n[5] & 0x7) << 8) | ((n[4] & 0x8) << 8)
                | ((n[2] & 0x7) << 4) | ((n[1] & 0x8) << 4)
                | (n[4] & 0x7) | (n[3] & 0x8);

            byte? compare = null;
            int data;
            if (code.Length == 8)
            {
                // 8-letter codes wrap their "data" bit around letter 8 instead of letter 6.
                data = ((n[1] & 0x7) << 4) | ((n[0] & 0x8) << 4)
                    | (n[0] & 0x7) | (n[7] & 0x8);

                compare = (byte)(
                    ((n[7] & 0x7) << 4) | ((n[6] & 0x8) << 4)
                    | (n[6] & 0x7) | (n[5] & 0x8));
            }
            else
            {
                data = ((n[1] & 0x7) << 4) | ((n[0] & 0x8) << 4)
                    | (n[0] & 0x7) | (n[5] & 0x8);
            }

            result = new GenieCode(code, (ushort)address, (byte)data, compare);
            return true;
        }
    }
}