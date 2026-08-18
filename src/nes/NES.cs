namespace NES
{
    public class NES
    {
        // public console interface
        public Cartridge Cartridge => cartridge;
        public Bus Bus => bus;
        public Timing Timing => timing;
        public long CPUCycle => bus.MasterCycle;

        // private console interface
        private Timing timing;
        private Cartridge cartridge;
        private Bus bus;

        public void Reset()
        {
            bus.CPU.Reset();
            bus.PPU.Reset();
            bus.APU.Reset();
            cartridge.Mapper.Reset();
        }

        public void Run()
        {
            long frameStart = bus.MasterCycle;
            while (bus.MasterCycle - frameStart < timing.CpuCyclesPerFrame)
            {
                bus.CPU.ExecuteInstruction();
            }
        }

        public NES(Cartridge cartridge, TvSystem tvSystem)
        {
            this.cartridge = cartridge;
            timing = Timing.For(tvSystem);
            bus = new Bus(cartridge, tvSystem);
            bus.CPU.Reset();
            Console.WriteLine("NES");
        }

        public NES(Cartridge cartridge) : this(cartridge, cartridge.tvSystem)
        {
        }
    }
}