namespace NES
{
    public struct MMC1MapperState
    {
        public byte shiftRegister;
        public byte control;
        public byte chrBank0;
        public byte chrBank1;
        public byte prgBank;
        public int shiftCount;

        public MMC1MapperState()
        {
            shiftRegister = 0x10;
            control = 0x0C;
            chrBank0 = chrBank1 = prgBank = 0;
            shiftCount = 0;
        }

        public MMC1MapperState(MMC1MapperState other)
        {
            shiftRegister = other.shiftRegister;
            control = other.control;
            chrBank0 = other.chrBank0;
            chrBank1 = other.chrBank1;
            prgBank = other.prgBank;
            shiftCount = other.shiftCount;
        }

        public MMC1MapperState Clone()
        {
            return new MMC1MapperState(this);
        }
    }

    public class MMC1Mapper : IMapper //MMC1 (Experimenal)
    {
        public const int MAPPER_TYPE_ID = 1;

        public MMC1MapperState State
        {
            get => state;
            set
            {
                state = value;
                ApplyMirroring();
                ApplyBanks();
            }
        }

        private MMC1MapperState state;
        private Cartridge cartridge;

        private int prgBankOffset0, prgBankOffset1;
        private int chrBankOffset0, chrBankOffset1;

        public MMC1Mapper(Cartridge cart)
        {
            cartridge = cart;
            state = new MMC1MapperState();
            //Reset();
        }

        public void Reset()
        {
            state = new MMC1MapperState();
            ApplyMirroring();
            ApplyBanks();
        }

        public byte CPURead(ushort addr)
        {
            if (addr >= 0x6000 && addr <= 0x7FFF)
            {
                return cartridge.prgRAM[addr - 0x6000];
            }
            else if (addr >= 0x8000 && addr <= 0xBFFF)
            {
                int index = prgBankOffset0 + (addr - 0x8000);
                return cartridge.prgROM[index];
            }
            else if (addr >= 0xC000 && addr <= 0xFFFF)
            {
                int index = prgBankOffset1 + (addr - 0xC000);
                return cartridge.prgROM[index];
            }
            return 0;
        }

        public void CPUWrite(ushort addr, byte val)
        {
            if (addr >= 0x6000 && addr <= 0x7FFF)
            {
                cartridge.prgRAM[addr - 0x6000] = val;
                return;
            }

            if (addr < 0x8000) return;

            if ((val & 0x80) != 0)
            {
                state.shiftRegister = 0x10;
                state.control |= 0x0C;
                state.shiftCount = 0;
                ApplyBanks();
                return;
            }

            state.shiftRegister = (byte)((state.shiftRegister >> 1) | ((val & 1) << 4));
            state.shiftCount++;

            if (state.shiftCount == 5)
            {
                int reg = (addr >> 13) & 0x03;
                switch (reg)
                {
                    case 0:
                        state.control = (byte)(state.shiftRegister & 0x1F);
                        ApplyMirroring();
                        break;
                    case 1:
                        state.chrBank0 = (byte)(state.shiftRegister & 0x1F);
                        ApplyMirroring();
                        break;
                    case 2:
                        state.chrBank1 = (byte)(state.shiftRegister & 0x1F);
                        break;
                    case 3:
                        state.prgBank = (byte)(state.shiftRegister & 0x0F);
                        break;
                }
                state.shiftRegister = 0x10;
                state.shiftCount = 0;
                ApplyBanks();
            }
        }

        public byte PPURead(ushort addr)
        {
            if (addr < 0x2000)
            {
                if (cartridge.chrBanks == 0)
                {
                    return cartridge.chrRAM[addr];
                }

                int chrMode = (state.control >> 4) & 1;
                if (chrMode == 0)
                {
                    int offset = (state.chrBank0 & 0x1E) * 0x1000;
                    return cartridge.chrROM[(addr + offset) % cartridge.chrROM.Length];
                }
                else
                {
                    if (addr < 0x1000)
                    {
                        return cartridge.chrROM[(addr + chrBankOffset0) % cartridge.chrROM.Length];
                    }
                    else
                    {
                        return cartridge.chrROM[((addr - 0x1000) + chrBankOffset1) % cartridge.chrROM.Length];
                    }
                }
            }
            return 0;
        }

        public void PPUWrite(ushort addr, byte val)
        {
            if (addr < 0x2000 && cartridge.chrBanks == 0)
            {
                cartridge.chrRAM[addr] = val;
            }
        }

        private void ApplyMirroring()
        {
            switch (state.control & 0x03)
            {
                case 0: cartridge.SetMirroring(Mirroring.SingleScreenA); break;
                case 1: cartridge.SetMirroring(Mirroring.SingleScreenB); break;
                case 2: cartridge.SetMirroring(Mirroring.Vertical); break;
                case 3: cartridge.SetMirroring(Mirroring.Horizontal); break;
            }
        }

        private void ApplyBanks()
        {
            int chrMode = (state.control >> 4) & 1;
            if (chrMode == 0)
            {
                chrBankOffset0 = (state.chrBank0 & 0x1E) * 0x1000;
                chrBankOffset1 = chrBankOffset0 + 0x1000;
            }
            else
            {
                chrBankOffset0 = state.chrBank0 * 0x1000;
                chrBankOffset1 = state.chrBank1 * 0x1000;
            }

            if (cartridge.chrBanks > 0)
            {
                chrBankOffset0 %= cartridge.chrROM.Length;
                chrBankOffset1 %= cartridge.chrROM.Length;
            }

            int prgMode = (state.control >> 2) & 0x03;
            int prgBankCount = cartridge.prgROM.Length / 0x4000;

            switch (prgMode)
            {
                case 0:
                case 1:
                    int bank = (state.prgBank & 0x0E) % Math.Max(1, prgBankCount);
                    prgBankOffset0 = bank * 0x4000;
                    prgBankOffset1 = prgBankOffset0 + 0x4000;
                    break;
                case 2:
                    prgBankOffset0 = 0;
                    prgBankOffset1 = (state.prgBank % Math.Max(1, prgBankCount)) * 0x4000;
                    break;
                case 3:
                    prgBankOffset0 = (state.prgBank % Math.Max(1, prgBankCount)) * 0x4000;
                    prgBankOffset1 = (prgBankCount - 1) * 0x4000;
                    break;
            }

            prgBankOffset0 %= cartridge.prgROM.Length;
            prgBankOffset1 %= cartridge.prgROM.Length;
        }
    }
}