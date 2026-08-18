using NES;
using System.Text;

public class SaveState
{
    private const string MAGIC = "NETNESSAVE";
    private const int VERSION = 6; // Version 1: No APU
                                   // Version 2: Broken APU state
                                   // Version 3: Proper APU state
                                   // Version 4: Input state
                                   // Version 5: Added controller #2
                                   // Version 6: Added missing ppu and bus states

    private byte[] romHash = new byte[32]; // sha256

    private PPUState ppuState;
    private CPUState cpuState;

    private bool haveFullBusState = false; // Previously only RAM was stored
    private BusState busState;

    private bool haveApuState = false;
    private APUState apuState;

    private bool haveInputState = false;
    private InputState inputState;

    private byte[] cartridgeRAM;
    private byte[] chrRAM;

    private int mapperTypeID = 0;

    // Mapper data
    private bool haveUxRomState = false;
    private UxROMMapperState uxRomState;

    private bool haveMMC1State = false;
    private MMC1MapperState mmc1State;

    private bool haveMMC3State = false;
    private MMC3MapperState mmc3State;

    private static PulseChannelState ReadPulseChannelState(BinaryReader reader)
    {
        return new PulseChannelState
        {
            duty = reader.ReadInt32(),
            envelopeLoop = reader.ReadBoolean(),
            constantVolume = reader.ReadBoolean(),
            volume = reader.ReadInt32(),

            sweepEnabled = reader.ReadBoolean(),
            sweepPeriod = reader.ReadInt32(),
            sweepNegate = reader.ReadBoolean(),
            sweepShift = reader.ReadInt32(),
            sweepReload = reader.ReadBoolean(),
            sweepDivider = reader.ReadInt32(),

            timer = reader.ReadInt32(),
            timerReload = reader.ReadInt32(),

            sequence = reader.ReadInt32(),

            lengthCounter = reader.ReadInt32(),
            enabled = reader.ReadBoolean(),

            envelopeDivider = reader.ReadInt32(),
            envelopeDecay = reader.ReadInt32(),
            envelopeStart = reader.ReadBoolean(),
        };
    }

    private static void WritePulseChannelState(BinaryWriter writer, PulseChannelState state)
    {
        writer.Write(state.duty);
        writer.Write(state.envelopeLoop);
        writer.Write(state.constantVolume);
        writer.Write(state.volume);

        writer.Write(state.sweepEnabled);
        writer.Write(state.sweepPeriod);
        writer.Write(state.sweepNegate);
        writer.Write(state.sweepShift);
        writer.Write(state.sweepReload);
        writer.Write(state.sweepDivider);

        writer.Write(state.timer);
        writer.Write(state.timerReload);

        writer.Write(state.sequence);

        writer.Write(state.lengthCounter);
        writer.Write(state.enabled);

        writer.Write(state.envelopeDivider);
        writer.Write(state.envelopeDecay);
        writer.Write(state.envelopeStart);
    }

    public void Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);

        // Header
        string magic = new string(reader.ReadChars(MAGIC.Length));
        if (magic != MAGIC)
        {
            throw new InvalidDataException("Invalid save state magic.");
        } 

        int version = reader.ReadInt32();
        if (version > VERSION)
        {
            throw new InvalidDataException($"Unsupported save state version: {version}");
        }

        romHash = reader.ReadBytes(32);

        // Reset data
        haveFullBusState = false;
        haveApuState = false;
        haveUxRomState = false;
        haveMMC3State = false;
        haveMMC1State = false;

        busState = new BusState();
        ppuState = new PPUState();
        cpuState = new CPUState();

        // Read save state data
        if (version >= 1)
        {
            /* CPU */ {
                // CPU
                cpuState = new CPUState
                {
                    A = reader.ReadByte(),
                    X = reader.ReadByte(),
                    Y = reader.ReadByte(),
                    SP = reader.ReadUInt16(),
                    PC = reader.ReadUInt16(),
                    status = reader.ReadByte(),
                    irqRequested = reader.ReadBoolean(),
                    nmiRequested = reader.ReadBoolean(),
                };

            }
            /* RAM */ {
                // CHR RAM (only present for cartridges that use it - NROM/CNROM
                // etc. with CHR-ROM will have written a zero-length array).
                int chrRamSize = reader.ReadInt32();
                if (chrRamSize < 0 || chrRamSize > 1024 * 1024) throw new InvalidDataException("Invalid CHR RAM size.");

                chrRAM = reader.ReadBytes(chrRamSize);
                if (chrRAM.Length != chrRamSize) throw new EndOfStreamException();

                // Cartridge RAM
                int cartRamSize = reader.ReadInt32();
                if (cartRamSize < 0 || cartRamSize > 1024 * 1024) throw new InvalidDataException("Invalid cartridge RAM size.");

                cartridgeRAM = reader.ReadBytes(cartRamSize);
                if (cartridgeRAM.Length != cartRamSize) throw new EndOfStreamException();

                // RAM
                int ramSize = reader.ReadInt32();
                if (ramSize < 0 || ramSize > 1024 * 1024) throw new InvalidDataException("Invalid RAM size.");

                busState.ram = reader.ReadBytes(ramSize);
                if (busState.ram.Length != ramSize) throw new EndOfStreamException();

            }
            /* PPU */ {
                var vram = reader.ReadBytes(2048);
                var paletteRam = reader.ReadBytes(32);
                var oam = reader.ReadBytes(256);

                var ppuctrl = reader.ReadByte();
                var ppumask = reader.ReadByte();
                var ppustatus = reader.ReadByte();
                var oamaddr = reader.ReadByte();
                var oamdata = reader.ReadByte();
                var ppuscrollx = reader.ReadByte();
                var ppuscrolly = reader.ReadByte();
                var ppuaddr = reader.ReadUInt16();
                var ppudata = reader.ReadByte();

                reader.ReadBoolean(); // used to be addrLatch before latches were merged

                var ppuDataBuffer = reader.ReadByte();
                var fineX = reader.ReadByte();
                var writeLatch = reader.ReadBoolean();
                var v = reader.ReadUInt16();
                var t = reader.ReadUInt16();
                var scanlineCycle = reader.ReadInt32();
                var scanline = reader.ReadInt32();

                ppuState = new PPUState
                {
                    VRAM = vram,
                    PaletteRam = paletteRam,
                    OAM = oam,
                    PPUCTRL = (PPUCtrlFlags)ppuctrl,
                    PPUMASK = (PPUMaskFlags)ppumask,
                    PPUSTATUS = (PPUStatusFlags)ppustatus,
                    OAMADDR = oamaddr,
                    OAMDATA = oamdata,
                    PPUSCROLLX = ppuscrollx,
                    PPUSCROLLY = ppuscrolly,
                    PPUADDR = ppuaddr,
                    PPUDATA = ppudata,
                    ppuDataBuffer = ppuDataBuffer,
                    fineX = fineX,
                    writeLatch = writeLatch,
                    v = v,
                    t = t,
                    scanlineCycle = scanlineCycle,
                    scanline = scanline
                };
            }
            /* Mapper */ {
                int mapperType = reader.ReadByte();
                switch (mapperType)
                {
                    case UxROMMapper.MAPPER_TYPE_ID:
                        haveUxRomState = true;

                        uxRomState = new UxROMMapperState()
                        {
                            prgBank = reader.ReadByte()
                        };
                        break;
                    case MMC1Mapper.MAPPER_TYPE_ID:
                        haveMMC1State = true;

                        mmc1State = new MMC1MapperState
                        {
                            shiftRegister = reader.ReadByte(),
                            control = reader.ReadByte(),
                            chrBank0 = reader.ReadByte(),
                            chrBank1 = reader.ReadByte(),
                            prgBank = reader.ReadByte(),
                            shiftCount = reader.ReadInt32(),
                        };
                        break;
                    case MMC3Mapper.MAPPER_TYPE_ID:
                        haveMMC3State = true;

                        mmc3State = new MMC3MapperState
                        {
                            bankSelect = reader.ReadByte(),

                            bankData = reader.ReadBytes(8),

                            prgMode = reader.ReadBoolean(),
                            chrMode = reader.ReadBoolean(),
                            prgRamEnable = reader.ReadBoolean(),
                            prgRamWriteProtect = reader.ReadBoolean(),

                            irqLatch = reader.ReadByte(),
                            irqCounter = reader.ReadByte(),
                            irqEnable = reader.ReadBoolean(),
                            irqReloadPending = reader.ReadBoolean(),
                            irqAsserted = reader.ReadBoolean(),

                            mirroring = (Mirroring)reader.ReadInt32()
                        };
                        break;
                }
            }
        }
        if(version >= 3)
        {
            /*APU*/ {
                haveApuState = true;
                apuState = new APUState
                {
                    pulse1 = ReadPulseChannelState(reader),
                    pulse2 = ReadPulseChannelState(reader),
                    triangle = new TriangleChannelState
                    {
                        controlFlag = reader.ReadBoolean(),
                        linearReloadValue = reader.ReadInt32(),

                        linearCounter = reader.ReadInt32(),
                        linearReloadFlag = reader.ReadBoolean(),

                        timer = reader.ReadInt32(),
                        timerReload = reader.ReadInt32(),

                        sequence = reader.ReadInt32(),

                        lengthCounter = reader.ReadInt32(),
                        enabled = reader.ReadBoolean(),
                    },
                    noise = new NoiseChannelState
                    {
                        envelopeLoop = reader.ReadBoolean(),
                        constantVolume = reader.ReadBoolean(),
                        volume = reader.ReadInt32(),

                        modeFlag = reader.ReadBoolean(),
                        timer = reader.ReadInt32(),
                        timerReload = reader.ReadInt32(),

                        shiftRegister = reader.ReadInt32(),

                        lengthCounter = reader.ReadInt32(),
                        enabled = reader.ReadBoolean(),

                        envelopeDivider = reader.ReadInt32(),
                        envelopeDecay = reader.ReadInt32(),
                        envelopeStart = reader.ReadBoolean(),
                    },
                    dmc = new DmcChannelState
                    {
                        irqEnable = reader.ReadBoolean(),
                        loop = reader.ReadBoolean(),
                        timerReload = reader.ReadInt32(),
                        timer = reader.ReadInt32(),

                        outputLevel = reader.ReadInt32(),

                        sampleAddress = reader.ReadUInt16(),
                        sampleLength = reader.ReadUInt16(),

                        currentAddress = reader.ReadUInt16(),
                        bytesRemaining = reader.ReadInt32(),
                        sampleBuffer = reader.ReadByte(),
                        sampleBufferFilled = reader.ReadBoolean(),

                        shiftRegister = reader.ReadInt32(),
                        bitsRemaining = reader.ReadInt32(),
                        silence = reader.ReadBoolean(),

                        irqFlag = reader.ReadBoolean(),
                    },

                    cpuCycle = reader.ReadInt32(),
                    sampleTimer = reader.ReadDouble(),

                    frameCycle = reader.ReadInt32(),
                    frameStep = reader.ReadInt32(),
                    fiveStepMode = reader.ReadBoolean(),
                    frameIrqInhibit = reader.ReadBoolean(),
                    frameIrqFlag = reader.ReadBoolean(),
                };

            }
        }
        if(version >= 4)
        {
            // Input block, on version 4, this only covers the first controller
            // On version 5, controller #2 was added
            /*Input*/
            {
                haveInputState = true;

                inputState = new InputState();
                inputState.strobe = reader.ReadBoolean();

                inputState.controller1.controllerState = reader.ReadByte();
                inputState.controller1.controllerShift = reader.ReadByte();
            }
        }
        if (version >= 5)
        {
            /*Input*/
            {
                inputState.controller2.controllerState = reader.ReadByte();
                inputState.controller2.controllerShift = reader.ReadByte();
            }
        }
        if (version >= 6)
        {
            haveFullBusState = true;

            ppuState.openBus = reader.ReadByte();
            ppuState.suppressVblankThisFrame = reader.ReadBoolean();
            ppuState.oamEvalAddr = reader.ReadByte();
            ppuState.oddFrame = reader.ReadBoolean();

            cpuState.nmiLine = reader.ReadBoolean();
            cpuState.nmiEdgeCycle = reader.ReadInt64();
            cpuState.lastInstructionEndCycle = reader.ReadInt64();

            busState.openBus = reader.ReadByte();
            busState.masterCycle = reader.ReadInt64();
            busState.ppuCycleRemainder = reader.ReadInt32();

            //tvSystem = (TvSystem)reader.ReadByte();
        }
    }

    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);

        // Header
        writer.Write(Encoding.ASCII.GetBytes(MAGIC));
        writer.Write(VERSION);
        writer.Write(romHash);

        // CPU
        writer.Write(cpuState.A);
        writer.Write(cpuState.X);
        writer.Write(cpuState.Y);
        writer.Write(cpuState.SP);
        writer.Write(cpuState.PC);
        writer.Write(cpuState.status);
        writer.Write(cpuState.irqRequested);
        writer.Write(cpuState.nmiRequested);

        // RAM
        writer.Write(chrRAM.Length);
        writer.Write(chrRAM);

        writer.Write(cartridgeRAM.Length);
        writer.Write(cartridgeRAM);

        writer.Write(busState.ram.Length);
        writer.Write(busState.ram);

        // PPU
        writer.Write(ppuState.VRAM);
        writer.Write(ppuState.PaletteRam);
        writer.Write(ppuState.OAM);

        writer.Write((byte)ppuState.PPUCTRL);
        writer.Write((byte)ppuState.PPUMASK);
        writer.Write((byte)ppuState.PPUSTATUS);
        writer.Write(ppuState.OAMADDR);
        writer.Write(ppuState.OAMDATA);
        writer.Write(ppuState.PPUSCROLLX);
        writer.Write(ppuState.PPUSCROLLY);
        writer.Write(ppuState.PPUADDR);
        writer.Write(ppuState.PPUDATA);

        writer.Write(false); // used to be addrLatch before latches were merged
        writer.Write(ppuState.ppuDataBuffer);

        writer.Write(ppuState.fineX);
        writer.Write(ppuState.writeLatch);
        writer.Write(ppuState.v);
        writer.Write(ppuState.t);

        writer.Write(ppuState.scanlineCycle);
        writer.Write(ppuState.scanline);

        // Mapper
        writer.Write((byte)mapperTypeID);
        if (haveMMC1State)
        {
            writer.Write(mmc1State.shiftRegister);
            writer.Write(mmc1State.control);
            writer.Write(mmc1State.chrBank0);
            writer.Write(mmc1State.chrBank1);
            writer.Write(mmc1State.prgBank);
            writer.Write(mmc1State.shiftCount);
        }
        else if (haveUxRomState)
        {
            writer.Write(uxRomState.prgBank);
        }
        else if (haveMMC3State)
        {
            writer.Write(mmc3State.bankSelect);
            writer.Write(mmc3State.bankData);

            writer.Write(mmc3State.prgMode);
            writer.Write(mmc3State.chrMode);
            writer.Write(mmc3State.prgRamEnable);
            writer.Write(mmc3State.prgRamWriteProtect);

            writer.Write(mmc3State.irqLatch);
            writer.Write(mmc3State.irqCounter);
            writer.Write(mmc3State.irqEnable);
            writer.Write(mmc3State.irqReloadPending);
            writer.Write(mmc3State.irqAsserted);

            writer.Write((int)mmc3State.mirroring);
        }

        // V 2/3 Fields (APU)
        WritePulseChannelState(writer, apuState.pulse1);
        WritePulseChannelState(writer, apuState.pulse2);

        writer.Write(apuState.triangle.controlFlag);
        writer.Write(apuState.triangle.linearReloadValue);

        writer.Write(apuState.triangle.linearCounter);
        writer.Write(apuState.triangle.linearReloadFlag);

        writer.Write(apuState.triangle.timer);
        writer.Write(apuState.triangle.timerReload);

        writer.Write(apuState.triangle.sequence);

        writer.Write(apuState.triangle.lengthCounter);
        writer.Write(apuState.triangle.enabled);

        writer.Write(apuState.noise.envelopeLoop);
        writer.Write(apuState.noise.constantVolume);
        writer.Write(apuState.noise.volume);

        writer.Write(apuState.noise.modeFlag);
        writer.Write(apuState.noise.timer);
        writer.Write(apuState.noise.timerReload);

        writer.Write(apuState.noise.shiftRegister);

        writer.Write(apuState.noise.lengthCounter);
        writer.Write(apuState.noise.enabled);

        writer.Write(apuState.noise.envelopeDivider);
        writer.Write(apuState.noise.envelopeDecay);
        writer.Write(apuState.noise.envelopeStart);

        writer.Write(apuState.dmc.irqEnable);
        writer.Write(apuState.dmc.loop);
        writer.Write(apuState.dmc.timerReload);
        writer.Write(apuState.dmc.timer);

        writer.Write(apuState.dmc.outputLevel);

        writer.Write(apuState.dmc.sampleAddress);
        writer.Write(apuState.dmc.sampleLength);

        writer.Write(apuState.dmc.currentAddress);
        writer.Write(apuState.dmc.bytesRemaining);
        writer.Write(apuState.dmc.sampleBuffer);
        writer.Write(apuState.dmc.sampleBufferFilled);

        writer.Write(apuState.dmc.shiftRegister);
        writer.Write(apuState.dmc.bitsRemaining);
        writer.Write(apuState.dmc.silence);

        writer.Write(apuState.dmc.irqFlag);

        writer.Write(apuState.cpuCycle);
        writer.Write(apuState.sampleTimer);

        writer.Write(apuState.frameCycle);
        writer.Write(apuState.frameStep);
        writer.Write(apuState.fiveStepMode);
        writer.Write(apuState.frameIrqInhibit);
        writer.Write(apuState.frameIrqFlag);

        // V4/5 Fields
        writer.Write(inputState.strobe);
        writer.Write(inputState.controller1.controllerState);
        writer.Write(inputState.controller1.controllerShift);
        writer.Write(inputState.controller2.controllerState);
        writer.Write(inputState.controller2.controllerShift);

        // V6 Fields
        writer.Write(ppuState.openBus.Read());
        writer.Write(ppuState.suppressVblankThisFrame);
        writer.Write(ppuState.oamEvalAddr);
        writer.Write(ppuState.oddFrame);

        writer.Write(cpuState.nmiLine);
        writer.Write(cpuState.nmiEdgeCycle);
        writer.Write(cpuState.lastInstructionEndCycle);

        writer.Write(busState.openBus);
        writer.Write(busState.masterCycle);
        writer.Write(busState.ppuCycleRemainder);
    }

    public void Apply(NES.NES nes)
    {
        var cart = nes.Cartridge;

        /* Apply Mapper */ {
            if (haveMMC3State && cart.Mapper is MMC3Mapper mmc3Mapper)
                mmc3Mapper.State = mmc3State;
            else if (haveMMC1State && cart.Mapper is MMC1Mapper mmc1Mapper)
                mmc1Mapper.State = mmc1State;
            else if (haveUxRomState && cart.Mapper is UxROMMapper uxRomMapper)
                uxRomMapper.State = uxRomState;
        }

        // Apply RAM
        if(!haveFullBusState)
        {
            // only have RAM
            var currentState = nes.Bus.State;
            Array.Copy(busState.ram, currentState.ram, Math.Min(currentState.ram.Length, busState.ram.Length));
            nes.Bus.State = currentState;
        }
        if (cartridgeRAM != null)
        {
            Array.Copy(
                cartridgeRAM,
                cart.prgRAM,
                Math.Min(cartridgeRAM.Length, cart.prgRAM.Length));
        }
        if (chrRAM != null)
        {
            Array.Copy(
                chrRAM,
                cart.chrRAM,
                Math.Min(chrRAM.Length, cart.chrRAM.Length));
        }

        // Apply States
        if (haveFullBusState) nes.Bus.State = busState;
        nes.Bus.PPU.State = ppuState;
        nes.Bus.CPU.State = cpuState;
        if (haveApuState) nes.Bus.APU.State = apuState;
        if (haveInputState) nes.Bus.Input.State = inputState;
    }

    public SaveState(NES.NES nes)
    {
        busState = nes.Bus.State;
        cpuState = nes.Bus.CPU.State;
        ppuState = nes.Bus.PPU.State;
        apuState = nes.Bus.APU.State;
        inputState = nes.Bus.Input.State;
        haveApuState = true;
        haveInputState = true;
        haveFullBusState = true;

        var cart = nes.Cartridge;
        romHash = cart.GetRomHash();
        mapperTypeID = cart.mapperID;
        cartridgeRAM = cart.prgRAM.ToArray();
        chrRAM = cart.chrRAM.ToArray();

        if (cart.Mapper is MMC1Mapper mmc1mapper)
        {
            haveMMC1State = true;
            this.mmc1State = mmc1mapper.State;
        }
        if (cart.Mapper is MMC3Mapper mmc3Mapper)
        {
            haveMMC3State = true;
            this.mmc3State = mmc3Mapper.State;
        }
        else if (cart.Mapper is UxROMMapper uxROMMapper)
        {
            haveUxRomState = true;
            this.uxRomState = uxROMMapper.State;
        }
    }
}
