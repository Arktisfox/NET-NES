namespace NES
{
    public struct BusState
    {
        public byte[] ram; //2KB RAM

        public byte openBus;
        public long masterCycle;
        public int ppuCycleRemainder;

        public BusState()
        {
            ram = new byte[2048];
            openBus = 0x00;
            masterCycle = 0x00;
            ppuCycleRemainder = 0;
        }

        public BusState(BusState other)
        {
            ram = new byte[other.ram.Length];
            Array.Copy(other.ram, ram, ram.Length);

            openBus = other.openBus;
            masterCycle = other.masterCycle;
            ppuCycleRemainder = other.ppuCycleRemainder;
        }

        public BusState Clone()
        {
            return new BusState(this);
        }
    }

    public class Bus : IBus
    {
        // Public interface
        public BusState State
        {
            get => state.Clone();
            set => state = value.Clone();
        }

        public APU APU => apu;
        public CPU CPU => cpu;
        public PPU PPU => ppu;
        public Cartridge Cartridge => cartridge;
        public Input Input => input;
        public byte OpenBus => state.openBus;
        public long MasterCycle => state.masterCycle;
        public ReadOnlySpan<byte> RAM => state.ram;

        // Private data
        private BusState state;

        private APU apu;
        private CPU cpu;
        private PPU ppu;
        private Cartridge cartridge;
        private Input input;

        private bool IsGetCycle => ((MasterCycle & 1) == 0) == GetCycleIsEven;
        private bool NextCycleIsGet => (((MasterCycle + 1) & 1) == 0) == GetCycleIsEven;

        private Timing timing;

        private int accessesThisInstruction;
        private bool tickInProgress;
        private bool dmcFetchInProgress;
        private ushort lastAccessAddress;

        // Tweaks
        private const bool LatchAccessAtEndOfCycle = false;
        private const bool GetCycleIsEven = true;
        private const bool StrobeAtEndOfWriteCycle = true;

        public Bus(Cartridge cartridge, TvSystem tvSystem)
        {
            this.state = new BusState();
            this.cartridge = cartridge;
            this.timing = Timing.For(tvSystem);
            this.cpu = new CPU(this);
            this.ppu = new PPU(this, tvSystem);
            this.apu = new APU(this, tvSystem);
            this.input = new Input(this);
            
            Console.WriteLine("Bus init");
        }

        public Bus(Cartridge cartridge) : this(cartridge, cartridge.tvSystem)
        {
        }

        public byte Read(ushort address)
        {
            accessesThisInstruction++;
            if (LatchAccessAtEndOfCycle) Tick();

            byte result = state.openBus;
            bool updatesOpenBus = true;

            if (address == 0x4015)
            {
                // Bit 5 of the APU status register is unused/undefined and
                // reads back as open bus rather than any real APU state.
                result = (byte)((apu.ReadStatus() & 0xDF) | (state.openBus & 0x20));
                updatesOpenBus = false;
            }
            else if (address == 0x4016)
            {
                result = (byte)((state.openBus & 0xE0) | input.Read4016());
            }
            else if (address == 0x4017)
            {
                result = (byte)((state.openBus & 0xE0) | input.Read4017());
            }
            else if (address >= 0x2000 && address <= 0x3FFF)
            {
                ushort reg = (ushort)(0x2000 + (address & 0x0007));
                result = ppu.ReadPPURegister(reg);
            }
            else if (address >= 0x0000 && address < 0x2000)
            {
                result = state.ram[address & 0x07FF];
            }
            else if (address >= 0x6000 && address <= 0xFFFF)
            {
                result = cartridge.CPURead(address);
            }

            if (updatesOpenBus) state.openBus = result;
            lastAccessAddress = address;

            if (!LatchAccessAtEndOfCycle) Tick();
            return result;
        }

        public void Write(ushort address, byte value)
        {
            accessesThisInstruction++;
            if (LatchAccessAtEndOfCycle) Tick();

            state.openBus = value;
            lastAccessAddress = address;
            bool pendingStrobe = false;

            if (address == 0x4016)
            {
                if (StrobeAtEndOfWriteCycle) pendingStrobe = true;
                else input.Write4016(value);
            }
            else if (address == 0x4014)
            {
                if (!LatchAccessAtEndOfCycle) Tick();
                Tick();
                RunOamDma(value);
                return;
            }
            else if (address >= 0x2000 && address <= 0x3FFF)
            {
                ushort reg = (ushort)(0x2000 + (address & 0x0007));
                ppu.WritePPURegister(reg, value);
            }
            else if (address >= 0x4000 && address <= 0x4017)
            {
                apu.Write(address, value);
            }
            else if (address >= 0x0000 && address < 0x2000)
            {
                state.ram[address & 0x07FF] = value;
            }
            else if (address >= 0x6000 && address <= 0xFFFF)
            {
                cartridge.CPUWrite(address, value);
            }

            if (!LatchAccessAtEndOfCycle) Tick();
            if (pendingStrobe) input.Write4016(value);
        }

        

        private void ConsumeCycle()
        {
            accessesThisInstruction++;
            Tick();
        }

        public void BeginInstruction()
        {
            accessesThisInstruction = 0;
        }

        public void EndInstruction(int totalCycles)
        {
            int padding = totalCycles - accessesThisInstruction;
            for (int i = 0; i < padding; i++)
            {
                Tick();
            }
        }

        // OAM DMA ($4014): steals 513 CPU cycles (514 if it starts on an odd
        // CPU cycle) - one alignment cycle, then 256 read/write pairs.
        private void RunOamDma(byte page)
        {
            int alignmentCycles = IsGetCycle ? 1 : 2;
            for (int i = 0; i < alignmentCycles; i++)
            {
                ConsumeCycle();
            }

            ushort baseAddr = (ushort)(page << 8);
            for (int i = 0; i < 256; i++)
            {
                byte value = Read((ushort)(baseAddr + i)); // real read cycle
                ppu.WriteOAMByte(value);
                ConsumeCycle(); // the matching write cycle
            }
        }

        private void RunDmcDma()
        {
            ConsumeCycle(); // halt cycle
            ConsumeCycle(); // dummy cycle

            if (!NextCycleIsGet)
            {
                ConsumeCycle(); // alignment: the fetch must land on a get cycle
            }

            byte value = Read(apu.DMCFetchAddress);
            apu.DoDMCFetch(value);
        }

        public void Tick()
        {
            if (tickInProgress)
            {
                return;
            }

            tickInProgress = true;
            state.masterCycle++;

            int ppuScaled = timing.PpuCyclesNumerator + state.ppuCycleRemainder;
            int ppuCycles = ppuScaled / timing.PpuCyclesDenominator;
            state.ppuCycleRemainder = ppuScaled % timing.PpuCyclesDenominator;

            ppu.Step(ppuCycles);
            apu.Step(1);
            tickInProgress = false;

            if (!dmcFetchInProgress && apu.DMCFetchPending)
            {
                dmcFetchInProgress = true;
                try
                {
                    RunDmcDma();
                }
                finally
                {
                    dmcFetchInProgress = false;
                }
            }
        }
    }
}