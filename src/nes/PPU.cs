namespace NES
{
    [Flags]
    public enum PPUCtrlFlags : byte
    {
        None = 0,
        NametableX = 1 << 0,
        NametableY = 1 << 1,
        Increment32 = 1 << 2,
        SpritePattern = 1 << 3,
        BackgroundPattern = 1 << 4,
        SpriteSize = 1 << 5,
        MasterSlave = 1 << 6,
        GenerateNMI = 1 << 7,
        All = NametableX | NametableY | Increment32 | SpritePattern | BackgroundPattern
              | SpriteSize | MasterSlave | GenerateNMI
    }

    [Flags]
    public enum PPUMaskFlags : byte
    {
        None = 0,
        Grayscale = 1 << 0,
        ShowBackgroundLeft = 1 << 1,
        ShowSpritesLeft = 1 << 2,
        ShowBackground = 1 << 3,
        ShowSprites = 1 << 4,
        EmphasizeRed = 1 << 5,
        EmphasizeGreen = 1 << 6,
        EmphasizeBlue = 1 << 7,
        All = Grayscale | ShowBackgroundLeft | ShowSpritesLeft | ShowSprites | EmphasizeRed | EmphasizeGreen | EmphasizeBlue
    }

    [Flags]
    public enum PPUStatusFlags : byte
    {
        None = 0,
        SpriteOverflow = 1 << 5,
        Sprite0Hit = 1 << 6,
        VBlank = 1 << 7,
        All = SpriteOverflow | Sprite0Hit | VBlank
    }

    public struct PPUState
    {
        public byte[] VRAM;
        public byte[] PaletteRam;
        public byte[] OAM;

        public PPUCtrlFlags PPUCTRL; //$2000
        public PPUMaskFlags PPUMASK; //$2001
        public PPUStatusFlags PPUSTATUS; //$2002

        public byte OAMADDR; //$2003
        public byte OAMDATA; //$2004
        public byte PPUSCROLLX, PPUSCROLLY; //$2005
        public byte PPUDATA; //$2007

        public byte ppuDataBuffer;
        public DecayingByte openBus; // last value written to any PPU register ($2007 data bus / open bus)

        // Set when $2002 is read on the exact PPU cycle immediately before
        // the vblank flag would be set. On real hardware this suppresses
        // both the flag and the NMI for that vblank period.
        public bool suppressVblankThisFrame;

        public byte fineX; //x
        public bool writeLatch; //w
        public ushort v; //current VRAM address
        public ushort t; //temp VRAM address

        // OAMADDR as it stood when evaluation began. Latched because the real
        // clear at 257-320 happens after evaluation, not before it.
        public byte oamEvalAddr;

        // NTSC skips dot 340 of the pre-render scanline on odd frames when
        // rendering is enabled. Tracked here so save states stay consistent.
        public bool oddFrame;

        public int scanlineCycle;
        public int scanline;

        // There is only one VRAM address register on hardware (v). PPUADDR is
        // kept as an alias so existing save-state code keeps working, but no
        // PPU logic reads or writes it any more - it used to drift out of sync
        // with v on the first $2006 write.
        public ushort PPUADDR
        {
            get => v;
            set => v = value;
        }

        public PPUState()
        {
            VRAM = new byte[2048];
            PaletteRam = new byte[32];
            OAM = new byte[256];

            PPUCTRL = 0x00;
            PPUSTATUS = 0x00;
            PPUMASK = 0x00;

            ppuDataBuffer = 0x00;
            openBus = 0x00;
            suppressVblankThisFrame = false;

            oamEvalAddr = 0x00;
            scanlineCycle = 0;
            scanline = 0;
            oddFrame = false;

            OAMADDR = 0x00;
            OAMDATA = 0x00;
            PPUSCROLLX = 0x00;
            PPUSCROLLY = 0x00;
            PPUDATA = 0x00;

            v = 0;
            t = 0;
            fineX = 0x00;
            writeLatch = false;
        }

        public PPUState(PPUState other)
        {
            VRAM = other.VRAM.ToArray();
            PaletteRam = other.PaletteRam.ToArray();
            OAM = other.OAM.ToArray();

            PPUCTRL = other.PPUCTRL;
            PPUMASK = other.PPUMASK;
            PPUSTATUS = other.PPUSTATUS;
            OAMADDR = other.OAMADDR;
            OAMDATA = other.OAMDATA;
            PPUSCROLLX = other.PPUSCROLLX;
            PPUSCROLLY = other.PPUSCROLLY;
            PPUDATA = other.PPUDATA;

            oamEvalAddr = other.oamEvalAddr;
            ppuDataBuffer = other.ppuDataBuffer;
            openBus = other.openBus;
            suppressVblankThisFrame = other.suppressVblankThisFrame;

            fineX = other.fineX;
            writeLatch = other.writeLatch;
            v = other.v;
            t = other.t;
            oddFrame = other.oddFrame;

            scanlineCycle = other.scanlineCycle;
            scanline = other.scanline;
        }

        public PPUState Clone()
        {
            return new PPUState(this);
        }
    }

    public class PPU
    {
        public bool SkipSprite0HitCheck = false;
        public IReadOnlyList<Color> FrameBuffer => frameBuffer;

        public PPUState State
        {
            get => state.Clone();
            set
            {
                state = new PPUState(value);
                ResetPipeline();
                UpdateNmiLine();
            }
        }

        private PPUState state = new PPUState();
        private Bus bus;

        private readonly Color[] frameBuffer;

        // Constants
        public const int ScreenWidth = 256;
        public const int ScreenHeight = 240;
        private const int CyclesPerScanlines = 341;

        //NES 64 Color Palette
        static readonly byte[,] NesPaletteRGB = new byte[64, 3]
        {
            { 84, 84, 84 }, { 0, 30, 116 }, { 8, 16, 144 }, { 48, 0, 136 },
            { 68, 0, 100 }, { 92, 0, 48 }, { 84, 4, 0 }, { 60, 24, 0 },
            { 32, 42, 0 }, { 8, 58, 0 }, { 0, 64, 0 }, { 0, 60, 0 },
            { 0, 50, 60 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 },
            { 152, 150, 152 }, { 8, 76, 196 }, { 48, 50, 236 }, { 92, 30, 228 },
            { 136, 20, 176 }, { 160, 20, 100 }, { 152, 34, 32 }, { 120, 60, 0 },
            { 84, 90, 0 }, { 40, 114, 0 }, { 8, 124, 0 }, { 0, 118, 40 },
            { 0, 102, 120 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 },
            { 236, 238, 236 }, { 76, 154, 236 }, { 120, 124, 236 }, { 176, 98, 236 },
            { 228, 84, 236 }, { 236, 88, 180 }, { 236, 106, 100 }, { 212, 136, 32 },
            { 160, 170, 0 }, { 116, 196, 0 }, { 76, 208, 32 }, { 56, 204, 108 },
            { 56, 180, 204 }, { 60, 60, 60 }, { 0, 0, 0 }, { 0, 0, 0 },
            { 236, 238, 236 }, { 168, 204, 236 }, { 188, 188, 236 }, { 212, 178, 236 },
            { 236, 174, 236 }, { 236, 174, 212 }, { 236, 180, 176 }, { 228, 196, 144 },
            { 204, 210, 120 }, { 180, 222, 120 }, { 168, 226, 144 }, { 152, 226, 180 },
            { 160, 214, 228 }, { 160, 162, 160 }, { 0, 0, 0 }, { 0, 0, 0 }
        };
        private static readonly Color[][] EmphasisPalettes = BuildEmphasisPalettes();

        private readonly int totalScanlines;
        private readonly TvSystem tvSystem;

        // Background fetch pipeline.
        private byte bgNextTileId;
        private byte bgNextTileAttr;
        private byte bgNextTileLsb;
        private byte bgNextTileMsb;
        private ushort bgShifterPatternLo;
        private ushort bgShifterPatternHi;
        private ushort bgShifterAttribLo;
        private ushort bgShifterAttribHi;

        // Sprite pipeline: secondary OAM plus the eight output units.
        private readonly byte[] secondaryOAM = new byte[32];
        private int spriteCount;
        private readonly byte[] spriteShifterLo = new byte[8];
        private readonly byte[] spriteShifterHi = new byte[8];
        private readonly byte[] spriteAttr = new byte[8];
        private readonly int[] spriteX = new int[8];
        private bool spriteZeroInScanline;

        // The sprite 0 hit flag is raised one dot after the overlapping pixel
        // is emitted. If the sprite-hit timing sub-tests disagree by a constant,
        // this delay is the thing to adjust.
        private bool sprite0HitPending;

        private static Color[][] BuildEmphasisPalettes()
        {
            const double Attenuation = 0.746;

            Color[][] tables = new Color[8][];
            for (int e = 0; e < 8; e++)
            {
                tables[e] = new Color[64];
                bool emphR = (e & 0x01) != 0;
                bool emphG = (e & 0x02) != 0;
                bool emphB = (e & 0x04) != 0;

                for (int c = 0; c < 64; c++)
                {
                    double r = NesPaletteRGB[c, 0];
                    double g = NesPaletteRGB[c, 1];
                    double b = NesPaletteRGB[c, 2];

                    if (emphR) { g *= Attenuation; b *= Attenuation; }
                    if (emphG) { r *= Attenuation; b *= Attenuation; }
                    if (emphB) { r *= Attenuation; g *= Attenuation; }

                    tables[e][c] = new Color(
                        (byte)Math.Clamp(r, 0, 255),
                        (byte)Math.Clamp(g, 0, 255),
                        (byte)Math.Clamp(b, 0, 255));
                }
            }
            return tables;
        }

        public void Reset()
        {
            state.PPUCTRL = 0x00;
            state.PPUMASK = 0x00;
            state.writeLatch = false;
            state.fineX = 0;
            state.ppuDataBuffer = 0x00;
            ResetPipeline();
            UpdateNmiLine();
        }

        private void ResetPipeline()
        {
            bgNextTileId = 0;
            bgNextTileAttr = 0;
            bgNextTileLsb = 0;
            bgNextTileMsb = 0;
            bgShifterPatternLo = 0;
            bgShifterPatternHi = 0;
            bgShifterAttribLo = 0;
            bgShifterAttribHi = 0;

            spriteCount = 0;
            spriteZeroInScanline = false;
            sprite0HitPending = false;
            Array.Clear(spriteShifterLo, 0, 8);
            Array.Clear(spriteShifterHi, 0, 8);
            Array.Clear(spriteAttr, 0, 8);
            Array.Clear(spriteX, 0, 8);
        }

        private void UpdateNmiLine()
        {
            if (bus == null || bus.CPU == null) return;
            bool asserted = (state.PPUSTATUS & PPUStatusFlags.VBlank) != 0 &&
                            (state.PPUCTRL & PPUCtrlFlags.GenerateNMI) != 0;
            bus.CPU.SetNmiLine(asserted);
        }

        public void Step(int elapsedCycles)
        {
            for (int i = 0; i < elapsedCycles; i++)
            {
                StepOneDot();
            }
        }

        private void StepOneDot()
        {
            int preRender = totalScanlines - 1;
            bool renderingEnabled = (state.PPUMASK & (PPUMaskFlags.ShowBackground | PPUMaskFlags.ShowSprites)) != 0;
            bool visibleLine = state.scanline >= 0 && state.scanline < 240;
            bool renderLine = visibleLine || state.scanline == preRender;

            // Deferred by one dot from the pixel that caused it.
            if (sprite0HitPending)
            {
                state.PPUSTATUS |= PPUStatusFlags.Sprite0Hit;
                sprite0HitPending = false;
            }

            // Clear VBLANK / sprite 0 hit / sprite overflow at dot 1 of the  pre-render scanline
            if (state.scanline == preRender && state.scanlineCycle == 0)
            {
                state.PPUSTATUS &= ~(PPUStatusFlags.VBlank |
                                     PPUStatusFlags.Sprite0Hit |
                                     PPUStatusFlags.SpriteOverflow);
                state.suppressVblankThisFrame = false;
                UpdateNmiLine();
            }

            // Set VBLANK at dot 1 of scanline 241
            if (state.scanline == 241 && state.scanlineCycle == 0)
            {
                if (!state.suppressVblankThisFrame)
                {
                    state.PPUSTATUS |= PPUStatusFlags.VBlank;
                }
                state.suppressVblankThisFrame = false;
                UpdateNmiLine();
            }

            if (renderLine && renderingEnabled)
            {
                RunRenderPipeline(preRender);
            }

            // the sprite shifters should treat all sprites X positions as 0 if rendering
            // has already been disabled and remains that way during dot 339.
            if (renderLine && !renderingEnabled && state.scanlineCycle == 339)
            {
                for (int i = 0; i < 8; i++)
                {
                    spriteX[i] = 0;
                }
            }

            if (visibleLine && state.scanlineCycle >= 1 && state.scanlineCycle <= 256)
            {
                OutputPixel(renderingEnabled);
            }

            if (visibleLine && state.scanlineCycle == 260)
            {
                if (renderingEnabled && (bus.Cartridge.Mapper is MMC3Mapper mmc3))
                {
                    mmc3.RunScanlineIRQ();
                    if (mmc3.IRQPending())
                    {
                        bus.CPU.RequestIRQ(true);
                        mmc3.ClearIRQ();
                    }
                }
            }

            state.scanlineCycle++;

            // NTSC odd frames skip dot 340 of the pre-render scanline
            int lineLength = CyclesPerScanlines;
            if (tvSystem == TvSystem.NTSC
                && state.scanline == preRender
                && state.oddFrame
                && renderingEnabled)
            {
                lineLength = CyclesPerScanlines - 1;
            }

            if (state.scanlineCycle >= lineLength)
            {
                state.scanlineCycle = 0;

                // normalize scanline in case totalScanLines is different (e.g. loaded savestate)
                if (state.scanline >= totalScanlines)
                {
                    state.scanline %= totalScanlines;
                }

                if (state.scanline == preRender)
                {
                    state.oddFrame = !state.oddFrame;
                }

                state.scanline++;
                if (state.scanline >= totalScanlines)
                {
                    state.scanline = 0;
                }
            }
        }

        private void RunRenderPipeline(int preRender)
        {
            int cycle = state.scanlineCycle;

            if ((cycle >= 2 && cycle < 258) || (cycle >= 321 && cycle < 338))
            {
                UpdateShifters();

                switch ((cycle - 1) % 8)
                {
                    case 0:
                        LoadBackgroundShifters();
                        bgNextTileId = Read((ushort)(0x2000 | (state.v & 0x0FFF)));
                        break;

                    case 2:
                        {
                            byte attr = Read((ushort)(0x23C0
                                | (state.v & 0x0C00)
                                | ((state.v >> 4) & 0x38)
                                | ((state.v >> 2) & 0x07)));

                            if (((state.v >> 5) & 0x02) != 0) attr >>= 4; // coarse Y bit 1
                            if ((state.v & 0x02) != 0) attr >>= 2;        // coarse X bit 1
                            bgNextTileAttr = (byte)(attr & 0x03);
                            break;
                        }

                    case 4:
                        bgNextTileLsb = Read((ushort)(BgPatternBase + (bgNextTileId * 16) + FineY));
                        break;

                    case 6:
                        bgNextTileMsb = Read((ushort)(BgPatternBase + (bgNextTileId * 16) + FineY + 8));
                        break;

                    case 7:
                        {
                            ushort v = state.v;
                            IncrementX(ref v);
                            state.v = v;
                            break;
                        }
                }
            }

            if (cycle == 256)
            {
                IncrementY();
            }

            if (cycle == 257)
            {
                LoadBackgroundShifters();
                CopyXFromTToV();
            }

            // OAMADDR as it stood when evaluation began. Latched because the real
            // clear at 257-320 happens after evaluation, not before it.
            if (cycle == 65) state.oamEvalAddr = state.OAMADDR;

            // OAMADDR is held at zero across the sprite fetch phase.
            if (cycle >= 257 && cycle <= 320) state.OAMADDR = 0;

            // Two redundant nametable fetches at the end of the line. Harmless
            // here, but they are what mappers watching A12 see.
            if (cycle == 338 || cycle == 340)
            {
                bgNextTileId = Read((ushort)(0x2000 | (state.v & 0x0FFF)));
            }

            if (state.scanline == preRender && cycle >= 280 && cycle <= 304)
            {
                CopyYFromTToV();
            }

            // Sprites for the NEXT scanline are selected at dot 257 and their
            // pattern data fetched during 257-320.
            if (cycle == 257) EvaluateSprites(preRender);
            if (cycle >= 264 && cycle <= 320 && (cycle - 264) % 8 == 0)
            {
                FetchSpritePattern((cycle - 264) / 8, preRender);
            }
        }

        private int BgPatternBase => ((state.PPUCTRL & PPUCtrlFlags.BackgroundPattern) != 0) ? 0x1000 : 0x0000;
        private int FineY => (state.v >> 12) & 0x07;

        private void LoadBackgroundShifters()
        {
            bgShifterPatternLo = (ushort)((bgShifterPatternLo & 0xFF00) | bgNextTileLsb);
            bgShifterPatternHi = (ushort)((bgShifterPatternHi & 0xFF00) | bgNextTileMsb);

            // The attribute bits are only 2 bits per tile, so they get smeared
            // across all 8 pixels of the tile as they are loaded.
            bgShifterAttribLo = (ushort)((bgShifterAttribLo & 0xFF00) | ((bgNextTileAttr & 0x01) != 0 ? 0x00FF : 0x0000));
            bgShifterAttribHi = (ushort)((bgShifterAttribHi & 0xFF00) | ((bgNextTileAttr & 0x02) != 0 ? 0x00FF : 0x0000));
        }

        private void UpdateShifters()
        {
            bgShifterPatternLo = (ushort)(bgShifterPatternLo << 1);
            bgShifterPatternHi = (ushort)(bgShifterPatternHi << 1);
            bgShifterAttribLo = (ushort)(bgShifterAttribLo << 1);
            bgShifterAttribHi = (ushort)(bgShifterAttribHi << 1);

            if (state.scanlineCycle >= 1 && state.scanlineCycle < 258)
            {
                for (int i = 0; i < spriteCount; i++)
                {
                    if (spriteX[i] > 0)
                    {
                        spriteX[i]--;
                    }
                    else
                    {
                        spriteShifterLo[i] = (byte)(spriteShifterLo[i] << 1);
                        spriteShifterHi[i] = (byte)(spriteShifterHi[i] << 1);
                    }
                }
            }
        }

        private void EvaluateSprites(int preRender)
        {
            // The pre-render line evaluates for scanline 0. A sprite at OAM y
            // is shown on lines y+1..y+h, so nothing can ever land on line 0 -
            // using -1 here reproduces that instead of special-casing it.
            int evalLine = (state.scanline == preRender) ? -1 : state.scanline;

            Array.Fill(secondaryOAM, (byte)0xFF);
            spriteCount = 0;
            spriteZeroInScanline = false;

            int height = (state.PPUCTRL & PPUCtrlFlags.SpriteSize) != 0 ? 16 : 8;
            bool first = true;

            for (int entry = state.oamEvalAddr; entry < 256; entry += 4)
            {
                int diff = evalLine - state.OAM[entry];
                bool inRange = diff >= 0 && diff < height;

                if (inRange)
                {
                    if (spriteCount < 8)
                    {
                        // Sprite 0 is the first entry evaluated, whatever it is.
                        if (first) spriteZeroInScanline = true;

                        for (int b = 0; b < 4; b++)
                            secondaryOAM[spriteCount * 4 + b] = state.OAM[(entry + b) & 0xFF];

                        spriteCount++;
                    }
                    else
                    {
                        state.PPUSTATUS |= PPUStatusFlags.SpriteOverflow;
                        break;
                    }
                }

                first = false;
            }
        }

        private void FetchSpritePattern(int i, int preRender)
        {
            if (i >= spriteCount) return;

            int evalLine = (state.scanline == preRender) ? -1 : state.scanline;
            bool is8x16 = (state.PPUCTRL & PPUCtrlFlags.SpriteSize) != 0;
            int height = is8x16 ? 16 : 8;

            byte oamY = secondaryOAM[i * 4 + 0];
            byte tile = secondaryOAM[i * 4 + 1];
            byte attr = secondaryOAM[i * 4 + 2];
            byte oamX = secondaryOAM[i * 4 + 3];

            int row = evalLine - oamY;
            if ((attr & 0x80) != 0) row = height - 1 - row; // vertical flip

            // If this slot's fetch happens under a new height (sprite size
            // was disabled before this slot's cycle), and the sprite no longer
            // fits, don't allow it to contribute to the scanline
            if (row < 0 || row >= height)
            {
                spriteShifterLo[i] = 0;
                spriteShifterHi[i] = 0;
                spriteAttr[i] = attr;
                spriteX[i] = oamX;
                return;
            }

            int addr;
            if (is8x16)
            {
                addr = ((tile & 0x01) != 0 ? 0x1000 : 0x0000)
                     + ((((tile & 0xFE) + (row / 8)) & 0xFF) * 16)
                     + (row % 8);
            }
            else
            {
                addr = ((state.PPUCTRL & PPUCtrlFlags.SpritePattern) != 0 ? 0x1000 : 0x0000)
                     + (tile * 16)
                     + row;
            }

            byte lo = Read((ushort)addr);
            byte hi = Read((ushort)(addr + 8));

            if ((attr & 0x40) != 0) // horizontal flip
            {
                lo = ReverseBits(lo);
                hi = ReverseBits(hi);
            }

            spriteShifterLo[i] = lo;
            spriteShifterHi[i] = hi;
            spriteAttr[i] = attr;
            spriteX[i] = oamX;
        }

        private static byte ReverseBits(byte b)
        {
            b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
            b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
            b = (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
            return b;
        }

        private void OutputPixel(bool renderingEnabled)
        {
            int x = state.scanlineCycle - 1;

            int bgPixel = 0;
            int bgPalette = 0;

            if (renderingEnabled
                && (state.PPUMASK & PPUMaskFlags.ShowBackground) != 0
                && (x >= 8 || (state.PPUMASK & PPUMaskFlags.ShowBackgroundLeft) != 0))
            {
                ushort bitMux = (ushort)(0x8000 >> state.fineX);

                int p0 = (bgShifterPatternLo & bitMux) != 0 ? 1 : 0;
                int p1 = (bgShifterPatternHi & bitMux) != 0 ? 1 : 0;
                bgPixel = p0 | (p1 << 1);

                int a0 = (bgShifterAttribLo & bitMux) != 0 ? 1 : 0;
                int a1 = (bgShifterAttribHi & bitMux) != 0 ? 1 : 0;
                bgPalette = a0 | (a1 << 1);
            }

            int fgPixel = 0;
            int fgPalette = 0;
            bool fgPriority = false;
            bool sprite0Rendered = false;

            if (renderingEnabled
                && (state.PPUMASK & PPUMaskFlags.ShowSprites) != 0
                && (x >= 8 || (state.PPUMASK & PPUMaskFlags.ShowSpritesLeft) != 0))
            {
                for (int i = 0; i < spriteCount; i++)
                {
                    if (spriteX[i] != 0) continue;

                    int lo = (spriteShifterLo[i] & 0x80) != 0 ? 1 : 0;
                    int hi = (spriteShifterHi[i] & 0x80) != 0 ? 1 : 0;
                    int p = lo | (hi << 1);
                    if (p == 0) continue;

                    fgPixel = p;
                    fgPalette = (spriteAttr[i] & 0x03) + 4;
                    fgPriority = (spriteAttr[i] & 0x20) == 0;
                    if (i == 0 && spriteZeroInScanline) sprite0Rendered = true;
                    break;
                }
            }

            // Sprite 0 hit. Both pixels must be opaque
            if (sprite0Rendered && bgPixel != 0 && fgPixel != 0
                && x != 255 && !state.PPUSTATUS.HasFlag(PPUStatusFlags.Sprite0Hit))
            {
                sprite0HitPending = true;
            }

            if (SkipSprite0HitCheck && x == 0)
            {
                // legacy debug flag handling
                sprite0HitPending = true;
            }

            int pixel;
            int palette;

            if (bgPixel == 0 && fgPixel == 0) { pixel = 0; palette = 0; }
            else if (bgPixel == 0) { pixel = fgPixel; palette = fgPalette; }
            else if (fgPixel == 0) { pixel = bgPixel; palette = bgPalette; }
            else if (fgPriority) { pixel = fgPixel; palette = fgPalette; }
            else { pixel = bgPixel; palette = bgPalette; }

            frameBuffer[state.scanline * ScreenWidth + x] = LookupPalette(pixel, palette);
        }

        private Color LookupPalette(int pixel, int palette)
        {
            int index = (pixel == 0) ? 0 : (((palette << 2) | pixel) & 0x1F);
            return Colorize(state.PaletteRam[index]);
        }

        public void WritePPURegister(ushort address, byte value)
        {
            state.openBus = value;
            switch (address)
            {
                case 0x2000:
                    state.PPUCTRL = (PPUCtrlFlags)value;
                    state.t = (ushort)((state.t & 0xF3FF) | ((value & 0x03) << 10));
                    // Enabling bit 7 while the vblank flag is set asserts /NMI
                    UpdateNmiLine();
                    break;
                case 0x2001:
                    state.PPUMASK = (PPUMaskFlags)value;
                    break;
                case 0x2002:
                    // Writes to $2002 are ignored on hardware; only the open
                    // bus latch (set above) is affected.
                    break;
                case 0x2003:
                    state.OAMADDR = value;
                    break;
                case 0x2004:
                    if ((state.PPUMASK & (PPUMaskFlags.ShowBackground | PPUMaskFlags.ShowSprites)) != 0 &&
                        ((state.scanline >= 0 && state.scanline < 240) || state.scanline == totalScanlines - 1))
                    {
                        // No OAM write happens. Only the high 6 bits of OAMADDR advance,
                        // which is a +4 since the low two bits are untouched.
                        state.OAMADDR = (byte)((state.OAMADDR + 4) & 0xFC);
                    }
                    else
                    {
                        if ((state.OAMADDR & 0x03) == 0x02) value &= 0xE3;
                        state.OAMDATA = value;
                        state.OAM[state.OAMADDR++] = state.OAMDATA;
                    }
                    break;
                case 0x2005:
                    if (!state.writeLatch)
                    {
                        state.PPUSCROLLX = value;
                        state.fineX = (byte)(value & 0x07);
                        state.t = (ushort)((state.t & 0xFFE0) | (value >> 3));
                    }
                    else
                    {
                        state.PPUSCROLLY = value;
                        state.t = (ushort)((state.t & 0x8FFF) | ((value & 0x07) << 12));
                        state.t = (ushort)((state.t & 0xFC1F) | ((value & 0xF8) << 2));
                    }
                    state.writeLatch = !state.writeLatch;
                    break;
                case 0x2006:
                    if (!state.writeLatch)
                    {
                        // Bit 14 of t is cleared by the first write, and v is
                        // NOT updated yet - a $2007 access between the two
                        // writes still uses the old address.
                        state.t = (ushort)((state.t & 0x00FF) | ((value & 0x3F) << 8));
                    }
                    else
                    {
                        state.t = (ushort)((state.t & 0xFF00) | value);
                        state.v = state.t;
                    }
                    state.writeLatch = !state.writeLatch;
                    break;
                case 0x2007:
                    state.PPUDATA = value;
                    Write((ushort)(state.v & 0x3FFF), state.PPUDATA);
                    IncrementPPUAddress();
                    break;
            }
        }

        public byte ReadPPURegister(ushort address)
        {
            byte result = state.openBus;
            switch (address)
            {
                case 0x2002:
                    // Reading one PPU clock BEFORE the flag is set returns 0
                    if (state.scanline == 241 && state.scanlineCycle == 0)
                    {
                        state.suppressVblankThisFrame = true;
                    }

                    result = (byte)((byte)(state.PPUSTATUS & PPUStatusFlags.All) | (state.openBus & 0x1F));
                    state.PPUSTATUS &= ~PPUStatusFlags.VBlank;

                    UpdateNmiLine();
                    state.writeLatch = false;
                    state.openBus = result;
                    break;
                case 0x2004:
                    bool renderingEnabled = (state.PPUMASK & (PPUMaskFlags.ShowBackground | PPUMaskFlags.ShowSprites)) != 0;
                    bool visibleScanline = state.scanline >= 0 && state.scanline < 240;
                    int cycle = state.scanlineCycle;

                    if (renderingEnabled && visibleScanline && cycle >= 1 && cycle <= 64)
                    {
                        // Secondary OAM clear - the port is forced high for the whole phase.
                        result = 0xFF;
                    }
                    else if (renderingEnabled && visibleScanline && cycle >= 65 && cycle <= 255)
                    {
                        // Sprite evaluation: reads follow the entry n is currently on.
                        int evalOffset = (cycle - 65) / 2;
                        result = state.OAM[(byte)(evalOffset * 4)];
                    }
                    else if (renderingEnabled && visibleScanline && cycle >= 256 && cycle <= 320)
                    {
                        // Sprite pattern fetch phase.
                        result = 0xFF;
                    }
                    else
                    {
                        result = state.OAM[state.OAMADDR];
                    }

                    state.openBus = result;
                    break;
                case 0x2007:
                    {
                        ushort addr = (ushort)(state.v & 0x3FFF);
                        if (addr >= 0x3F00)
                        {
                            result = Read(addr);

                            // Palette RAM entries are only 6 bits wide in hardware, take last two from open bus
                            result = (byte)((result & 0x3F) | (state.openBus & 0xC0));

                            // Greyscale mode (PPUMASK bit 0) forces the low 4 bits to zero
                            if (state.PPUMASK.HasFlag(PPUMaskFlags.Grayscale))
                            {
                                result &= 0xF0;
                            }

                            // The read buffer is still filled from the nametable
                            // mirrored underneath the palette.
                            state.ppuDataBuffer = Read((ushort)(addr & 0x2FFF));
                        }
                        else
                        {
                            result = state.ppuDataBuffer;
                            state.ppuDataBuffer = Read(addr);
                        }

                        state.openBus = result;
                        IncrementPPUAddress();
                        return result;
                    }
                    // $2000/$2001/$2003/$2005/$2006 all read back as open bus,
                    // which is the default assigned above.
            }
            return result;
        }

        private void IncrementPPUAddress()
        {
            bool renderingEnabled = (state.PPUMASK & (PPUMaskFlags.ShowBackground | PPUMaskFlags.ShowSprites)) != 0;
            bool renderingScanline = state.scanline < 240 || state.scanline == (totalScanlines - 1);

            if (renderingEnabled && renderingScanline)
            {
                ushort v = state.v;
                IncrementX(ref v);
                state.v = v;
                IncrementY();
            }
            else
            {
                int step = (state.PPUCTRL & PPUCtrlFlags.Increment32) != 0 ? 32 : 1;
                state.v = (ushort)((state.v + step) & 0x7FFF); // v is 15 bits
            }
        }

        public byte Read(ushort address)
        {
            address = (ushort)(address & 0x3FFF);

            if (address < 0x2000)
            {
                return bus.Cartridge.PPURead(address);
            }
            else if (address >= 0x2000 && address <= 0x3EFF)
            {
                ushort mirrored = MirrorVRAMAddress(address);
                return state.VRAM[mirrored];
            }
            else if (address >= 0x3F00 && address <= 0x3FFF)
            {
                ushort mirrored = (ushort)(address & 0x1F);
                if (mirrored >= 0x10 && (mirrored % 4) == 0) mirrored -= 0x10;
                return state.PaletteRam[mirrored];
            }

            return 0;
        }

        public void Write(ushort address, byte value)
        {
            address = (ushort)(address & 0x3FFF);

            if (address < 0x2000)
            {
                bus.Cartridge.PPUWrite(address, value);
            }
            else if (address >= 0x2000 && address <= 0x3EFF)
            {
                ushort mirrored = MirrorVRAMAddress(address);
                state.VRAM[mirrored] = value;
            }
            else if (address >= 0x3F00 && address <= 0x3FFF)
            {
                ushort mirrored = (ushort)(address & 0x1F);
                if (mirrored >= 0x10 && (mirrored % 4) == 0) mirrored -= 0x10;
                state.PaletteRam[mirrored] = value;
            }
        }

        private ushort MirrorVRAMAddress(ushort address)
        {
            ushort offset = (ushort)(address & 0x0FFF);

            int ntIndex = offset / 0x400;
            int innerOffset = offset % 0x400;

            switch (bus.Cartridge.mirroringMode)
            {
                case Mirroring.Vertical:
                    return (ushort)((ntIndex % 2) * 0x400 + innerOffset);
                case Mirroring.Horizontal:
                    return (ushort)(((ntIndex / 2) * 0x400) + innerOffset);
                case Mirroring.SingleScreenA:
                    return (ushort)(innerOffset);
                case Mirroring.SingleScreenB:
                    return (ushort)(0x400 + innerOffset);
                default:
                    return offset;
            }
        }

        public void WriteOAMByte(byte value)
        {
            if ((state.OAMADDR & 0x03) == 0x02) value &= 0xE3;
            state.OAM[state.OAMADDR++] = value;
        }

        private void IncrementY()
        {
            if ((state.v & 0x7000) != 0x7000)
            {
                state.v += 0x1000;
            }
            else
            {
                state.v &= 0x8FFF;
                int y = (state.v & 0x03E0) >> 5;
                if (y == 29)
                {
                    y = 0;
                    state.v ^= 0x0800;
                }
                else if (y == 31)
                {
                    y = 0;
                }
                else
                {
                    y += 1;
                }
                state.v = (ushort)((state.v & 0xFC1F) | (y << 5));
            }
        }

        private void IncrementX(ref ushort addr)
        {
            if ((addr & 0x001F) == 31)
            {
                addr &= 0xFFE0;
                addr ^= 0x0400;
            }
            else
            {
                addr++;
            }
        }

        private void CopyXFromTToV()
        {
            state.v = (ushort)((state.v & 0xFBE0) | (state.t & 0x041F));
        }

        private void CopyYFromTToV()
        {
            state.v = (ushort)((state.v & 0x041F) | (state.t & 0x7BE0));
        }

        // Applies PPUMASK greyscale (bit 0) and the colour emphasis bits (5-7).
        // On PAL the red and green emphasis bits are swapped.
        private Color Colorize(byte paletteEntry)
        {
            int index = paletteEntry & 0x3F;
            if ((state.PPUMASK & PPUMaskFlags.Grayscale) != 0)
            {
                index &= 0x30;
            }

            int emphasis = ((byte)state.PPUMASK >> 5) & 0x07;
            if (tvSystem == TvSystem.PAL)
            {
                emphasis = (emphasis & 0x04) | ((emphasis & 0x01) << 1) | ((emphasis & 0x02) >> 1);
            }

            return EmphasisPalettes[emphasis][index];
        }

        public PPU(Bus bus, TvSystem tvSystem)
        {
            this.bus = bus;
            this.tvSystem = tvSystem;

            state = new PPUState();
            totalScanlines = Timing.For(tvSystem).ScanlinesPerFrame;
            frameBuffer = new Color[ScreenWidth * ScreenHeight];

            Console.WriteLine("PPU init");
        }
    }
}