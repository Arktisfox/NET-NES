namespace NES
{
    public struct MMC3MapperState
    {
        public byte bankSelect;
        public byte[] bankData = new byte[8];

        public bool prgMode;
        public bool chrMode;
        public bool prgRamEnable;
        public bool prgRamWriteProtect;

        public byte irqLatch;
        public byte irqCounter;
        public bool irqEnable;
        public bool irqReloadPending;
        public bool irqAsserted;

        public Mirroring mirroring;

        public MMC3MapperState()
        {
            bankSelect = 0;
            bankData = new byte[8];

            prgMode = false;
            chrMode = false;
            prgRamEnable = true;
            prgRamWriteProtect = false;

            irqLatch = 0;
            irqCounter = 0;
            irqEnable = false;
            irqReloadPending = false;
            irqAsserted = false;

            mirroring = Mirroring.Horizontal;


            for (int i = 0; i < bankData.Length; i++)
            {
                bankData[i] = 0;
            }
        }

        public MMC3MapperState(MMC3MapperState other)
        {
            bankSelect = other.bankSelect;
            bankData = other.bankData.ToArray();

            prgMode = other.prgMode;
            chrMode = other.chrMode;
            prgRamEnable = other.prgRamEnable;
            prgRamWriteProtect = other.prgRamWriteProtect;

            irqLatch = other.irqLatch;
            irqCounter = other.irqCounter;
            irqEnable = other.irqEnable;
            irqReloadPending = other.irqReloadPending;
            irqAsserted = other.irqAsserted;

            mirroring = other.mirroring;
        }

        public MMC3MapperState Clone()
        {
            return new MMC3MapperState(this);
        }
    }

    public class MMC3Mapper : IMapper  //MMC3 (Experimental)
    {
        public const int MAPPER_TYPE_ID = 4;

        private MMC3MapperState state;
        private Cartridge cartridge;

        private int[] prgBankOffsets = new int[4];
        private int[] chrBankOffsets = new int[8];

        public MMC3MapperState State
        {
            get => state.Clone();
            set
            {
                state = value.Clone();
                cartridge.SetMirroring(state.mirroring);
                ApplyBankMapping();
            }
        }

        public MMC3Mapper(Cartridge cart)
        {
            cartridge = cart;
            state = new MMC3MapperState();
            state.mirroring = cart.mirroringMode;
        }

        public void Reset()
        {
            state = new MMC3MapperState();
            ApplyBankMapping();
        }

        public void RunScanlineIRQ()
        {
            bool reload = state.irqReloadPending || state.irqCounter == 0;

            if (reload)
            {
                state.irqCounter = state.irqLatch;
                state.irqReloadPending = false;
            }
            else
            {
                state.irqCounter--;
            }

            if (state.irqCounter == 0 && state.irqEnable)
            {
                state.irqAsserted = true;
            }
        }

        public bool IRQPending()
        {
            return state.irqAsserted;
        }

        public void ClearIRQ()
        {
            state.irqAsserted = false;
        }

        public byte CPURead(ushort address)
        {
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                if (state.prgRamEnable)
                {
                    int ramOffset = (address - 0x6000) % cartridge.prgRAM.Length;
                    return cartridge.prgRAM[ramOffset];
                }
                return 0xFF;
            }

            if (address >= 0x8000 && address <= 0xFFFF)
            {
                int bankIndex = (address - 0x8000) / 0x2000;
                int bankOffset = prgBankOffsets[bankIndex];
                int addressOffset = address % 0x2000;

                int finalOffset = (bankOffset + addressOffset) % cartridge.prgROM.Length;
                return cartridge.prgROM[finalOffset];
            }

            return 0;
        }

        public void CPUWrite(ushort address, byte value)
        {
            if (address >= 0x6000 && address <= 0x7FFF)
            {
                if (state.prgRamEnable && !state.prgRamWriteProtect)
                {
                    int ramOffset = (address - 0x6000) % cartridge.prgRAM.Length;
                    cartridge.prgRAM[ramOffset] = value;
                }
                return;
            }

            switch (address & 0xE001)
            {
                case 0x8000:
                    state.bankSelect = value;
                    state.prgMode = (value & 0x40) != 0;
                    state.chrMode = (value & 0x80) != 0;
                    ApplyBankMapping();
                    break;
                case 0x8001:
                    int reg = state.bankSelect & 0x07;
                    state.bankData[reg] = value;
                    ApplyBankMapping();
                    break;
                case 0xA000:
                    state.mirroring = (value & 1) == 0
                        ? Mirroring.Vertical
                        : Mirroring.Horizontal;

                    cartridge.SetMirroring(state.mirroring);
                    break;
                case 0xA001:
                    state.prgRamEnable = (value & 0x80) != 0;
                    state.prgRamWriteProtect = (value & 0x40) != 0;
                    break;
                case 0xC000:
                    state.irqLatch = value;
                    break;
                case 0xC001:
                    state.irqReloadPending = true;
                    break;
                case 0xE000:
                    state.irqEnable = false;
                    state.irqAsserted = false;
                    break;
                case 0xE001:
                    state.irqEnable = true;
                    break;
            }
        }

        public byte PPURead(ushort address)
        {
            if (address >= 0x2000) return 0;

            if (cartridge.chrBanks == 0)
            {
                return cartridge.chrRAM[address % cartridge.chrRAM.Length];
            }

            int bank = address / 0x0400;
            int bankOffset = chrBankOffsets[bank];
            int addressOffset = address % 0x0400;

            int finalOffset = (bankOffset + addressOffset) % cartridge.chrROM.Length;
            return cartridge.chrROM[finalOffset];
        }

        public void PPUWrite(ushort address, byte value)
        {
            if (address < 0x2000)
            {
                if (cartridge.chrBanks == 0)
                {
                    cartridge.chrRAM[address] = value;
                }
            }
        }

        private void ApplyBankMapping()
        {
            if (state.chrMode)
            {
                chrBankOffsets[0] = state.bankData[2] * 0x400;
                chrBankOffsets[1] = state.bankData[3] * 0x400;
                chrBankOffsets[2] = state.bankData[4] * 0x400;
                chrBankOffsets[3] = state.bankData[5] * 0x400;

                chrBankOffsets[4] = (state.bankData[0] & 0xFE) * 0x400;
                chrBankOffsets[5] = chrBankOffsets[4] + 0x400;
                chrBankOffsets[6] = (state.bankData[1] & 0xFE) * 0x400;
                chrBankOffsets[7] = chrBankOffsets[6] + 0x400;
            }
            else
            {
                chrBankOffsets[0] = (state.bankData[0] & 0xFE) * 0x400;
                chrBankOffsets[1] = chrBankOffsets[0] + 0x400;
                chrBankOffsets[2] = (state.bankData[1] & 0xFE) * 0x400;
                chrBankOffsets[3] = chrBankOffsets[2] + 0x400;

                chrBankOffsets[4] = state.bankData[2] * 0x400;
                chrBankOffsets[5] = state.bankData[3] * 0x400;
                chrBankOffsets[6] = state.bankData[4] * 0x400;
                chrBankOffsets[7] = state.bankData[5] * 0x400;
            }

            int bankCount = cartridge.prgROM.Length / 0x2000;
            int lastBank = bankCount - 1;

            int bank6 = state.bankData[6] % bankCount;
            int bank7 = state.bankData[7] % bankCount;

            if (state.prgMode)
            {
                prgBankOffsets[0] = (lastBank - 1) * 0x2000;
                prgBankOffsets[1] = bank7 * 0x2000;
                prgBankOffsets[2] = bank6 * 0x2000;
                prgBankOffsets[3] = lastBank * 0x2000;
            }
            else
            {
                prgBankOffsets[0] = bank6 * 0x2000;
                prgBankOffsets[1] = bank7 * 0x2000;
                prgBankOffsets[2] = (lastBank - 1) * 0x2000;
                prgBankOffsets[3] = lastBank * 0x2000;
            }

            if (cartridge.chrBanks > 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    chrBankOffsets[i] %= cartridge.chrROM.Length;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                prgBankOffsets[i] %= cartridge.prgROM.Length;
            }
        }
    }
}