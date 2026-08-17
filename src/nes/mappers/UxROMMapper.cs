namespace NES
{
    public struct UxROMMapperState
    {
        public byte prgBank;

        public void Reset()
        {
            prgBank = 0;
        }

        public UxROMMapperState()
        {
            prgBank = 0;
        }

        public UxROMMapperState(UxROMMapperState other)
        {
            prgBank = other.prgBank;
        }

        public UxROMMapperState Clone()
        {
            return new UxROMMapperState(this);
        }
    }

    public class UxROMMapper : IMapper
    {
        public const int MAPPER_TYPE_ID = 2;

        private Cartridge cartridge;
        private UxROMMapperState state;

        public UxROMMapperState State
        {
            get => state;
            set => state = value;
        }

        public UxROMMapper(Cartridge cart)
        {
            cartridge = cart;
            state = new UxROMMapperState();
        }

        public void Reset()
        {
            state.Reset();
        }

        public byte CPURead(ushort addr)
        {
            if (addr >= 0x8000 && addr <= 0xBFFF)
            {
                int index = (state.prgBank * 0x4000) + (addr - 0x8000);
                return index < cartridge.prgROM.Length ? cartridge.prgROM[index] : (byte)0xFF;
            }
            else if (addr >= 0xC000 && addr <= 0xFFFF)
            {
                int fixedBankStart = cartridge.prgROM.Length - 0x4000;
                int index = fixedBankStart + (addr - 0xC000);
                return index < cartridge.prgROM.Length ? cartridge.prgROM[index] : (byte)0xFF;
            }
            return 0;
        }

        public void CPUWrite(ushort addr, byte val)
        {
            if (addr >= 0x8000)
            {
                state.prgBank = (byte)(val & 0x0F);
            }
        }

        public byte PPURead(ushort addr)
        {
            if (addr < 0x2000)
            {
                if (cartridge.chrBanks == 0)
                    return cartridge.chrRAM[addr];
                return cartridge.chrROM[addr % cartridge.chrROM.Length];
            }
            return 0;
        }

        public void PPUWrite(ushort addr, byte val)
        {
            if (cartridge.chrBanks == 0 && addr < 0x2000)
            {
                cartridge.chrRAM[addr] = val;
            }
        }
    }
}