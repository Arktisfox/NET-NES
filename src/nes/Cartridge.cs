using GameGenie;
using System.Security.Cryptography;

namespace NES
{ 
    public enum Mirroring
    {
        Horizontal,
        Vertical,
        SingleScreenA,
        SingleScreenB
    }

    public class Cartridge
    {
        // Cartridge data
        public byte[] rom;
        public byte[] romHash;
        private string? romPathHint = null;

        public byte[] prgROM = Array.Empty<byte>();
        public byte[] chrROM = Array.Empty<byte>();

        public byte[] prgRAM = Array.Empty<byte>();
        public byte[] chrRAM = Array.Empty<byte>();

        public int prgBanks;
        public int chrBanks;
        public int mapperID;
        public bool mirrorHorizontal;
        public bool mirrorVertical;
        public Mirroring mirroringMode;
        public bool hasBattery;

        // Timing
        public TvSystem tvSystem;
        public Timing Timing => Timing.For(tvSystem);

        // Mapper
        private IMapper mapper;
        public IMapper Mapper => mapper;

        // Cheats
        private readonly Dictionary<ushort, GenieCode> genieCodes = new();
        public IReadOnlyCollection<GenieCode> GenieCodes => genieCodes.Values;

        public bool AddGenieCode(string rawCode)
        {
            if (!GameGenie.GameGenie.TryParse(rawCode, out GenieCode genie)) return false;

            // If a code already targets this address, the new one replaces it -
            // matches how real Game Genie carts only have one override per byte.
            genieCodes[genie.Address] = genie;
            return true;
        }

        /// <returns>true if a matching active code was found and removed.</returns>
        public bool RemoveGenieCode(string code)
        {
            string normalized = code.Trim().ToUpperInvariant();
            foreach (var kvp in genieCodes)
            {
                if (kvp.Value.Code == normalized)
                {
                    genieCodes.Remove(kvp.Key);
                    return true;
                }
            }
            return false;
        }

        public void ClearGenieCodes()
        {
            genieCodes.Clear();
        }

        private TvSystem DetectTvSystem(byte[] rom, byte flag7)
        {
            bool isNes20 = (flag7 & 0x0C) == 0x08;

            if (isNes20 && rom.Length > 12)
            {
                // NES 2.0 byte 12, bits 0-1: 0 = NTSC, 1 = PAL, 2 = multi-region, 3 = Dendy.
                int region = rom[12] & 0x03;
                if (region == 1 || region == 3) return TvSystem.PAL;
                return TvSystem.NTSC;
            }

            if (rom.Length > 9)
            {
                // Informal iNES 1.0 extension, byte 9 bit 0: 0 = NTSC, 1 = PAL.
                if ((rom[9] & 0x01) != 0) return TvSystem.PAL;
            }

            return DetectTvSystemFromFileName(romPathHint) ?? TvSystem.NTSC;
        }

        private static TvSystem? DetectTvSystemFromFileName(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();

            if (name.Contains("(E)") || name.Contains("(EUROPE)") || name.Contains("(PAL)") ||
                name.Contains("(G)") || name.Contains("(F)") || name.Contains("(A)"))
            {
                return TvSystem.PAL;
            }

            if (name.Contains("(U)") || name.Contains("(USA)") || name.Contains("(J)") ||
                name.Contains("(JAPAN)") || name.Contains("(NTSC)"))
            {
                return TvSystem.NTSC;
            }

            return null;
        }

        public byte[] GetRomHash()
        {
            return romHash;
        }

        private void Load()
        {
            if (rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1A)
            {
                throw new InvalidDataException("Invalid iNES Header!");
            }

            prgBanks = rom[4];
            chrBanks = rom[5];

            byte flag6 = rom[6];
            byte flag7 = rom[7];

            mirrorVertical = (flag6 & 0x01) != 0;
            mirrorHorizontal = !mirrorVertical;
            hasBattery = (flag6 & 0x02) != 0;

            if ((flag6 & 0x08) != 0)
            {

            }
            else if ((flag6 & 0x01) != 0)
            {
                mirroringMode = Mirroring.Vertical;
            }
            else
            {
                mirroringMode = Mirroring.Horizontal;
            }

            mapperID = flag6 >> 4 | ((flag7 >> 4) << 4);
            tvSystem = DetectTvSystem(rom, flag7);

            int prgSize = prgBanks * 16 * 1024;
            int chrSize = chrBanks * 8 * 1024;

            int offset = 16; //iNES rom is 16 bytes
            prgROM = new byte[prgSize];
            Array.Copy(rom, offset, prgROM, 0, prgSize);

            offset += prgSize;
            chrROM = new byte[chrSize];
            Array.Copy(rom, offset, chrROM, 0, chrSize);

            prgRAM = new byte[8 * 1024];
            chrRAM = new byte[8 * 1024];

            switch (mapperID)
            {
                case NROMMapper.MAPPER_TYPE_ID:
                    mapper = new NROMMapper(this);
                    break;
                case MMC1Mapper.MAPPER_TYPE_ID:
                    mapper = new MMC1Mapper(this);
                    break;
                case UxROMMapper.MAPPER_TYPE_ID:
                    mapper = new UxROMMapper(this);
                    break;
                case MMC3Mapper.MAPPER_TYPE_ID:
                    mapper = new MMC3Mapper(this);
                    break;
                default:
                    Console.WriteLine("Mapper " + mapperID + " is not supported");
                    Environment.Exit(1);
                    break;
            }
            mapper.Reset();
        }

        public Cartridge(byte[] rom)
        {
            this.mapper = new NROMMapper(this);
            this.romPathHint = null;
            this.rom = rom;
            romHash = SHA256.Create().ComputeHash(rom);

            Load();
        }

        public Cartridge(string romPath)
        {
            this.mapper = new NROMMapper(this);
            this.romPathHint = romPath;
            this.rom = File.ReadAllBytes(romPath);
            romHash = SHA256.Create().ComputeHash(rom);

            Load();
        }

        public byte CPURead(ushort address)
        {
            byte value = mapper.CPURead(address);
            if (genieCodes.TryGetValue(address, out GenieCode genie))
            {
                if (!genie.Compare.HasValue || genie.Compare.Value == value)
                {
                    return genie.Data;
                }
            }
            return value;
        }

        public void CPUWrite(ushort address, byte value)
        {
            mapper.CPUWrite(address, value);
        }

        public byte PPURead(ushort address)
        {
            return mapper.PPURead(address);
        }
        public void PPUWrite(ushort address, byte value)
        {
            mapper.PPUWrite(address, value);
        }

        public void SetMirroring(Mirroring mode)
        {
            mirroringMode = mode;
            mirrorVertical = mode == Mirroring.Vertical;
            mirrorHorizontal = mode == Mirroring.Horizontal;
        }
    }


}