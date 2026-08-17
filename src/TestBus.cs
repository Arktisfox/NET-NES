using NES;

public class TestBus : IBus 
{
    public byte[] ram; //64 KB RAM

    public TestBus() {
        ram = new byte[65536];

        Console.WriteLine("Test Bus init");
    }

    public void Write(ushort address, byte value) {
        ram[address] = value;
    }

    public byte Read(ushort address) {
        return ram[address];
    }

    private long masterCycle;
    public long MasterCycle => masterCycle;

    // No PPU/APU to keep in sync here - these are only meaningful on the
    // real console Bus.
    public void BeginInstruction() { }
    public void EndInstruction(int totalCycles) { }
    public void Tick() { }
}