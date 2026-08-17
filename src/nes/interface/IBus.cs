namespace NES
{
    public interface IBus
    {
        void Write(ushort address, byte value);
        byte Read(ushort address);

        long MasterCycle { get; }

        void BeginInstruction();
        void EndInstruction(int totalCycles);
        void Tick();
    }
}