namespace NES
{
    public struct CPUState
    {
        public byte A, X, Y;
        public ushort PC, SP;
        public byte status; //Flags (P)

        public bool irqRequested;
        public bool nmiRequested;

        public bool nmiLine;
        public long nmiEdgeCycle;

        public long lastInstructionEndCycle;

        public CPUState()
        {
            A = X = Y = 0;
            PC = 0x0000;
            SP = 0x0000;
            status = 0;
            irqRequested = false;
            nmiRequested = false;
            nmiLine = false;
            nmiEdgeCycle = long.MinValue;
            lastInstructionEndCycle = 0;
        }
    }

    public class CPU
    {
        public CPUState State
        {
            get => state;
            set => state = value;
        }
        private CPUState state;

        private const int FLAG_C = 0; //Carry
        private const int FLAG_Z = 1; //Zero
        private const int FLAG_I = 2; //Interrupt
        private const int FLAG_D = 3; //Decimal Mode (Unused in NES)
        private const int FLAG_B = 4; //Break Command
        private const int FLAG_UNUSED = 5; //Used bit 5 (always set)
        private const int FLAG_V = 6; //Overflow
        private const int FLAG_N = 7; //Negative

        private IBus bus;

        private static bool doFudge = false;
        private static Random fudger = new Random();
        private static double adcFudgeChance = 0.0001;
        private static double jmpFudgeChance = 0.00001;

        public bool EnableGlitches
        {
            get => doFudge;
            set => doFudge = value;
        }

        public CPU(IBus bus)
        {
            this.bus = bus;
            state = new CPUState();

            EnableGlitches = false;
            Console.WriteLine("CPU init");
        }

        public void Reset()
        {
            state.SP = (ushort)((state.SP - 3) & 0x00FF);
            SetFlag(FLAG_I, true);

            byte low = bus.Read(0xFFFC);
            byte high = bus.Read(0xFFFD);
            state.PC = (ushort)((high << 8) | low);
        }

        public void SetFlag(int bit, bool value)
        {
            if (value)
            {
                state.status |= (byte)(1 << bit);
            }
            else
            {
                state.status &= (byte)~(1 << bit);
            }
        }

        public bool GetFlag(int bit)
        {
            return (state.status & (1 << bit)) != 0;
        }

        public void SetZN(byte value)
        {
            SetFlag(FLAG_Z, value == 0); //Zero
            SetFlag(FLAG_N, (value & 0x80) != 0); //Negative
        }

        /*
        public void Log() {
            ushort op1 = (PC);
            ushort op2 = (ushort)(PC + 1);
            ushort op3 = (ushort)(PC + 2);
            ushort op4 = (ushort)(PC + 3);
            //Console.WriteLine("A: " + A.ToString("X2") + " X: " + X.ToString("X2") + " Y: " + Y.ToString("X2") + " SP: " + SP.ToString("X4") + " PC: " + "00:" + PC.ToString("X4") + " (" + mmu.Read(op1).ToString("X2") + " " + mmu.Read(op2).ToString("X2") + " " + mmu.Read(op3).ToString("X2") + " " + mmu.Read(op4).ToString("X2") + ")");
            byte b1 = bus.Read(op1);
            byte b2 = bus.Read(op2);
            byte b3 = bus.Read(op3);
            byte b4 = bus.Read(op4);

            Console.WriteLine("A: " + A.ToString("X2") + " X: " + X.ToString("X2") +" Y: " + Y.ToString("X2") +  "P: "+ Convert.ToString(status, 2).PadLeft(8, '0') + " SP: " + SP.ToString("X4") + " PC: :" + PC.ToString("X4") + " (" + b1.ToString("X2") + " " + b2.ToString("X2") + " " + b3.ToString("X2") + " " + b4.ToString("X2") + ")");
        }
        */

        private byte Fetch()
        {
            return bus.Read(state.PC++);
        }

        public ushort Fetch16Bits()
        {
            byte low = Fetch();
            byte high = Fetch();
            return (ushort)((high << 8) | low);
        }

        public void RequestIRQ(bool line)
        {
            state.irqRequested = line;
        }

        public void SetNmiLine(bool asserted)
        {
            if (asserted && !state.nmiLine)
            {
                state.nmiRequested = true;
                state.nmiEdgeCycle = bus.MasterCycle;
            }
            else if (!asserted)
            {
                // The line dropped. If the CPU has not yet polled this edge
                // (i.e. it arrived during the instruction currently running),
                // the NMI never happens.
                if (state.nmiRequested && state.nmiEdgeCycle > state.lastInstructionEndCycle - 1)
                {
                    state.nmiRequested = false;
                    state.nmiEdgeCycle = long.MinValue;
                }
            }

            state.nmiLine = asserted;
        }

        public int ExecuteInstruction()
        {
            // An NMI edge is only visible to this instruction's interrupt
            // poll if it arrived at or before the second-to-last cycle of the
            // previous instruction.
            bool nmiVisible = state.nmiRequested && state.nmiEdgeCycle <= state.lastInstructionEndCycle - 1;

            bus.BeginInstruction();
            int used = ExecuteInstructionCore(nmiVisible);
            bus.EndInstruction(used);
            state.lastInstructionEndCycle = bus.MasterCycle;
            return used;
        }

        private int ExecuteInstructionCore(bool nmiVisible)
        {
            // NMI takes priority over IRQ. There is only one path now: an NMI
            // raised by vblank onset and one raised by a $2000 write during
            // vblank are the same event as far as the CPU is concerned.
            if (nmiVisible)
            {
                state.nmiRequested = false;
                state.nmiEdgeCycle = long.MinValue;
                return NMI();
            }

            if (GetFlag(FLAG_I) == false && state.irqRequested)
            {
                state.irqRequested = false;
                return IRQ();
            }

            ushort instructionStart = state.PC;

            byte opcode = Fetch();

            switch (opcode)
            {
                //BRK, NOP, RTI
                case 0x00: return BRK();
                case 0xEA: return NOP();
                case 0x40: return RTI();

                //LDA, LDX, LDY, STA, STX, STY
                case 0xA9: return LDR(ref state.A, Immediate, 2);
                case 0xA5: return LDR(ref state.A, ZeroPage, 3);
                case 0xB5: return LDR(ref state.A, ZeroPageX, 4);
                case 0xAD: return LDR(ref state.A, Absolute, 4);
                case 0xBD: return LDR(ref state.A, AbsoluteX, 4);
                case 0xB9: return LDR(ref state.A, AbsoluteY, 4);
                case 0xA1: return LDR(ref state.A, IndirectX, 6);
                case 0xB1: return LDR(ref state.A, IndirectY, 5);
                case 0xA2: return LDR(ref state.X, Immediate, 2);
                case 0xA6: return LDR(ref state.X, ZeroPage, 3);
                case 0xB6: return LDR(ref state.X, ZeroPageY, 4);
                case 0xAE: return LDR(ref state.X, Absolute, 4);
                case 0xBE: return LDR(ref state.X, AbsoluteY, 4);
                case 0xA0: return LDR(ref state.Y, Immediate, 2);
                case 0xA4: return LDR(ref state.Y, ZeroPage, 3);
                case 0xB4: return LDR(ref state.Y, ZeroPageX, 4);
                case 0xAC: return LDR(ref state.Y, Absolute, 4);
                case 0xBC: return LDR(ref state.Y, AbsoluteX, 4);
                case 0x85: return STR(ref state.A, ZeroPage, 3);
                case 0x95: return STR(ref state.A, ZeroPageX, 4);
                case 0x8D: return STR(ref state.A, Absolute, 4);
                case 0x9D: return STR(ref state.A, AbsoluteXDummyRead, 5);
                case 0x99: return STR(ref state.A, AbsoluteYDummyRead, 5);
                case 0x81: return STR(ref state.A, IndirectX, 6);
                case 0x91: return STR(ref state.A, IndirectYDummyRead, 6);
                case 0x86: return STR(ref state.X, ZeroPage, 3);
                case 0x96: return STR(ref state.X, ZeroPageY, 4);
                case 0x8E: return STR(ref state.X, Absolute, 4);
                case 0x84: return STR(ref state.Y, ZeroPage, 3);
                case 0x94: return STR(ref state.Y, ZeroPageX, 4);
                case 0x8C: return STR(ref state.Y, Absolute, 4);

                //TAX, TAY, TXA, TYA
                case 0xAA: return TRR(ref state.X, ref state.A, Implied, 2);
                case 0xA8: return TRR(ref state.Y, ref state.A, Implied, 2);
                case 0x8A: return TRR(ref state.A, ref state.X, Implied, 2);
                case 0x98: return TRR(ref state.A, ref state.Y, Implied, 2);

                //TSX, TXS, PHA, PHP, PLA, PLP
                case 0xBA: return TSX(Implied, 2);
                case 0x9A: return TXS(Implied, 2);
                case 0x48: return PHA(Implied, 3);
                case 0x08: return PHP(Implied, 3);
                case 0x68: return PLA(Implied, 4);
                case 0x28: return PLP(Implied, 4);

                //AND, EOR, ORA, BIT
                case 0x29: return AND(Immediate, 2);
                case 0x25: return AND(ZeroPage, 3);
                case 0x35: return AND(ZeroPageX, 4);
                case 0x2D: return AND(Absolute, 4);
                case 0x3D: return AND(AbsoluteX, 4);
                case 0x39: return AND(AbsoluteY, 4);
                case 0x21: return AND(IndirectX, 6);
                case 0x31: return AND(IndirectY, 5);
                case 0x49: return EOR(Immediate, 2);
                case 0x45: return EOR(ZeroPage, 3);
                case 0x55: return EOR(ZeroPageX, 4);
                case 0x4D: return EOR(Absolute, 4);
                case 0x5D: return EOR(AbsoluteX, 4);
                case 0x59: return EOR(AbsoluteY, 4);
                case 0x41: return EOR(IndirectX, 6);
                case 0x51: return EOR(IndirectY, 5);
                case 0x09: return ORA(Immediate, 2);
                case 0x05: return ORA(ZeroPage, 3);
                case 0x15: return ORA(ZeroPageX, 4);
                case 0x0D: return ORA(Absolute, 4);
                case 0x1D: return ORA(AbsoluteX, 4);
                case 0x19: return ORA(AbsoluteY, 4);
                case 0x01: return ORA(IndirectX, 6);
                case 0x11: return ORA(IndirectY, 5);
                case 0x24: return BIT(ZeroPage, 3);
                case 0x2C: return BIT(Absolute, 4);

                //ADC, SBC, CMP, CPX, CPY
                case 0x69: return ADC(Immediate, 2);
                case 0x65: return ADC(ZeroPage, 3);
                case 0x75: return ADC(ZeroPageX, 4);
                case 0x6D: return ADC(Absolute, 4);
                case 0x7D: return ADC(AbsoluteX, 4);
                case 0x79: return ADC(AbsoluteY, 4);
                case 0x61: return ADC(IndirectX, 6);
                case 0x71: return ADC(IndirectY, 5);
                case 0xE9: return SBC(Immediate, 2);
                case 0xE5: return SBC(ZeroPage, 3);
                case 0xF5: return SBC(ZeroPageX, 4);
                case 0xED: return SBC(Absolute, 4);
                case 0xFD: return SBC(AbsoluteX, 4);
                case 0xF9: return SBC(AbsoluteY, 4);
                case 0xE1: return SBC(IndirectX, 6);
                case 0xF1: return SBC(IndirectY, 5);
                case 0xC9: return CPR(state.A, Immediate, 2);
                case 0xC5: return CPR(state.A, ZeroPage, 3);
                case 0xD5: return CPR(state.A, ZeroPageX, 4);
                case 0xCD: return CPR(state.A, Absolute, 4);
                case 0xDD: return CPR(state.A, AbsoluteX, 4);
                case 0xD9: return CPR(state.A, AbsoluteY, 4);
                case 0xC1: return CPR(state.A, IndirectX, 6);
                case 0xD1: return CPR(state.A, IndirectY, 5);
                case 0xE0: return CPR(state.X, Immediate, 2);
                case 0xE4: return CPR(state.X, ZeroPage, 3);
                case 0xEC: return CPR(state.X, Absolute, 4);
                case 0xC0: return CPR(state.Y, Immediate, 2);
                case 0xC4: return CPR(state.Y, ZeroPage, 3);
                case 0xCC: return CPR(state.Y, Absolute, 4);

                //INC, INX, INY, DEC, DEX, DEY
                case 0xE6: return INC(ZeroPage, 5);
                case 0xF6: return INC(ZeroPageX, 6);
                case 0xEE: return INC(Absolute, 6);
                case 0xFE: return INC(AbsoluteXDummyRead, 7);
                case 0xE8: return INR(ref state.X, Implied, 2);
                case 0xC8: return INR(ref state.Y, Implied, 2);
                case 0xC6: return DEC(ZeroPage, 5);
                case 0xD6: return DEC(ZeroPageX, 6);
                case 0xCE: return DEC(Absolute, 6);
                case 0xDE: return DEC(AbsoluteXDummyRead, 7);
                case 0xCA: return DER(ref state.X, Implied, 2);
                case 0x88: return DER(ref state.Y, Implied, 2);

                //ASL, LSR, ROL, ROR
                case 0x0A: return ASL(Accumulator, 2);
                case 0x06: return ASL(ZeroPage, 5);
                case 0x16: return ASL(ZeroPageX, 6);
                case 0x0E: return ASL(Absolute, 6);
                case 0x1E: return ASL(AbsoluteXDummyRead, 7);
                case 0x4A: return LSR(Accumulator, 2);
                case 0x46: return LSR(ZeroPage, 5);
                case 0x56: return LSR(ZeroPageX, 6);
                case 0x4E: return LSR(Absolute, 6);
                case 0x5E: return LSR(AbsoluteXDummyRead, 7);
                case 0x2A: return ROL(Accumulator, 2);
                case 0x26: return ROL(ZeroPage, 5);
                case 0x36: return ROL(ZeroPageX, 6);
                case 0x2E: return ROL(Absolute, 6);
                case 0x3E: return ROL(AbsoluteXDummyRead, 7);
                case 0x6A: return ROR(Accumulator, 2);
                case 0x66: return ROR(ZeroPage, 5);
                case 0x76: return ROR(ZeroPageX, 6);
                case 0x6E: return ROR(Absolute, 6);
                case 0x7E: return ROR(AbsoluteXDummyRead, 7);

                //JMP, JSR, RTS
                case 0x4C: return JMP(Absolute, 3);
                case 0x6C: return JMP(Indirect, 5);
                case 0x20: return JSR();
                case 0x60: return RTS();

                //BCC, BCS, BEQ, BMI, BNE, BPL, BVC, BVS
                case 0x90: return BIF(!GetFlag(FLAG_C), Relative, 2);
                case 0xB0: return BIF(GetFlag(FLAG_C), Relative, 2);
                case 0xF0: return BIF(GetFlag(FLAG_Z), Relative, 2);
                case 0x30: return BIF(GetFlag(FLAG_N), Relative, 2);
                case 0xD0: return BIF(!GetFlag(FLAG_Z), Relative, 2);
                case 0x10: return BIF(!GetFlag(FLAG_N), Relative, 2);
                case 0x50: return BIF(!GetFlag(FLAG_V), Relative, 2);
                case 0x70: return BIF(GetFlag(FLAG_V), Relative, 2);

                //CLC, CLD, CLI, CLV, SEC, SED, SEI
                case 0x18: return FSC(FLAG_C, false, Implied, 2);
                case 0xD8: return FSC(FLAG_D, false, Implied, 2);
                case 0x58: return FSC(FLAG_I, false, Implied, 2);
                case 0xB8: return FSC(FLAG_V, false, Implied, 2);
                case 0x38: return FSC(FLAG_C, true, Implied, 2);
                case 0xF8: return FSC(FLAG_D, true, Implied, 2);
                case 0x78: return FSC(FLAG_I, true, Implied, 2);

                //Unofficial NOPs
                case 0x1A: return NOP();
                case 0x3A: return NOP();
                case 0x5A: return NOP();
                case 0x7A: return NOP();
                case 0xDA: return NOP();
                case 0xFA: return NOP();

                case 0x80: return NOP(Immediate, 2);
                case 0x82: return NOP(Immediate, 2);
                case 0x89: return NOP(Immediate, 2);
                case 0xC2: return NOP(Immediate, 2);
                case 0xE2: return NOP(Immediate, 2);

                case 0x04: return NOP(ZeroPage, 3);
                case 0x44: return NOP(ZeroPage, 3);
                case 0x64: return NOP(ZeroPage, 3);

                case 0x14: return NOP(ZeroPageX, 4);
                case 0x34: return NOP(ZeroPageX, 4);
                case 0x54: return NOP(ZeroPageX, 4);
                case 0x74: return NOP(ZeroPageX, 4);
                case 0xD4: return NOP(ZeroPageX, 4);
                case 0xF4: return NOP(ZeroPageX, 4);

                case 0x0C: return NOP(Absolute, 4);

                case 0x1C: return NOP(AbsoluteX, 4);
                case 0x3C: return NOP(AbsoluteX, 4);
                case 0x5C: return NOP(AbsoluteX, 4);
                case 0x7C: return NOP(AbsoluteX, 4);
                case 0xDC: return NOP(AbsoluteX, 4);
                case 0xFC: return NOP(AbsoluteX, 4);

                //Unofficial: SLO (ASL + ORA)
                case 0x03: return SLO(IndirectX, 8);
                case 0x07: return SLO(ZeroPage, 5);
                case 0x0F: return SLO(Absolute, 6);
                case 0x13: return SLO(IndirectYDummyRead, 8);
                case 0x17: return SLO(ZeroPageX, 6);
                case 0x1B: return SLO(AbsoluteYDummyRead, 7);
                case 0x1F: return SLO(AbsoluteXDummyRead, 7);

                //Unofficial: RLA (ROL + AND)
                case 0x23: return RLA(IndirectX, 8);
                case 0x27: return RLA(ZeroPage, 5);
                case 0x2F: return RLA(Absolute, 6);
                case 0x33: return RLA(IndirectYDummyRead, 8);
                case 0x37: return RLA(ZeroPageX, 6);
                case 0x3B: return RLA(AbsoluteYDummyRead, 7);
                case 0x3F: return RLA(AbsoluteXDummyRead, 7);

                //Unofficial: SRE (LSR + EOR)
                case 0x43: return SRE(IndirectX, 8);
                case 0x47: return SRE(ZeroPage, 5);
                case 0x4F: return SRE(Absolute, 6);
                case 0x53: return SRE(IndirectYDummyRead, 8);
                case 0x57: return SRE(ZeroPageX, 6);
                case 0x5B: return SRE(AbsoluteYDummyRead, 7);
                case 0x5F: return SRE(AbsoluteXDummyRead, 7);

                //Unofficial: RRA (ROR + ADC)
                case 0x63: return RRA(IndirectX, 8);
                case 0x67: return RRA(ZeroPage, 5);
                case 0x6F: return RRA(Absolute, 6);
                case 0x73: return RRA(IndirectYDummyRead, 8);
                case 0x77: return RRA(ZeroPageX, 6);
                case 0x7B: return RRA(AbsoluteYDummyRead, 7);
                case 0x7F: return RRA(AbsoluteXDummyRead, 7);

                //Unofficial: DCP (DEC + CMP)
                case 0xC3: return DCP(IndirectX, 8);
                case 0xC7: return DCP(ZeroPage, 5);
                case 0xCF: return DCP(Absolute, 6);
                case 0xD3: return DCP(IndirectYDummyRead, 8);
                case 0xD7: return DCP(ZeroPageX, 6);
                case 0xDB: return DCP(AbsoluteYDummyRead, 7);
                case 0xDF: return DCP(AbsoluteXDummyRead, 7);

                //Unofficial: ISC/ISB (INC + SBC)
                case 0xE3: return ISC(IndirectX, 8);
                case 0xE7: return ISC(ZeroPage, 5);
                case 0xEF: return ISC(Absolute, 6);
                case 0xF3: return ISC(IndirectYDummyRead, 8);
                case 0xF7: return ISC(ZeroPageX, 6);
                case 0xFB: return ISC(AbsoluteYDummyRead, 7);
                case 0xFF: return ISC(AbsoluteXDummyRead, 7);

                //Unofficial: SBC duplicate
                case 0xEB: return SBC(Immediate, 2);

                //Unofficial: deterministic immediate-mode opcodes
                case 0x0B: return ANC(Immediate, 2);
                case 0x2B: return ANC(Immediate, 2);
                case 0x4B: return ASR(Immediate, 2);
                case 0x6B: return ARR(Immediate, 2);
                case 0xCB: return AXS(Immediate, 2);

                //Unofficial: unstable opcodes
                case 0x8B: return ANE(Immediate, 2);
                case 0xAB: return LXA(Immediate, 2);
                /*
                 * Couldn't get these working reliably, but they're unstable anyways
                 * 
                case 0x93: return SHA(IndirectY, 6);
                case 0x9F: return SHA(AbsoluteY, 5);
                case 0x9E: return SHX(AbsoluteY, 5);
                case 0x9C: return SHY(AbsoluteX, 5);
                case 0x9B: return SHS(AbsoluteY, 5);
                */
                case 0xBB: return LAE(AbsoluteY, 4);

                //Unofficial: SAX (store A & X)
                case 0x83: return SAX(IndirectX, 6);
                case 0x87: return SAX(ZeroPage, 3);
                case 0x8F: return SAX(Absolute, 4);
                case 0x97: return SAX(ZeroPageY, 4);

                //Unofficial: LAX (load A and X together)
                case 0xA3: return LAX(IndirectX, 6);
                case 0xA7: return LAX(ZeroPage, 3);
                case 0xAF: return LAX(Absolute, 4);
                case 0xB3: return LAX(IndirectY, 5);
                case 0xB7: return LAX(ZeroPageY, 4);
                case 0xBF: return LAX(AbsoluteY, 4);

                default:
                    Console.WriteLine("Unimplemented Opcode: " + opcode.ToString("X2") + " , PC: " + (state.PC - 1).ToString("X4"));
                    return 2; // Treat as a 2-cycle NOP so we don't stall the frame loop.
            }
        }

        // Load/Store Operations
        private int LDR(ref byte r, Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            //r = addr.value;
            r = bus.Read(addr.address);
            SetZN(r);

            return baseCycles + addr.extraCycles;
        }

        private int LAX(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            state.A = value;
            state.X = value;
            SetZN(value);

            return baseCycles + addr.extraCycles;
        }

        private int STR(ref byte r, Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            bus.Write(addr.address, r);
            return baseCycles; //No extra cycle to add
        }

        private int SAX(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            bus.Write(addr.address, (byte)(state.A & state.X));
            return baseCycles;
        }

        //Register Transfer
        private int TRR(ref byte r1, ref byte r2, Func<AddrResult> mode, int baseCycles)
        {
            r1 = r2;
            SetZN(r1);

            return baseCycles;
        }

        // Stack Operations
        private void StackPush(byte value)
        {
            bus.Write((ushort)(0x0100 + state.SP), value);
            state.SP--;
            state.SP &= 0x00FF;
        }

        private byte StackPop()
        {
            state.SP++;
            state.SP &= 0x00FF;
            return bus.Read((ushort)(0x0100 + state.SP));
        }

        private int TSX(Func<AddrResult> mode, int baseCycles)
        {
            state.X = (byte)state.SP;
            SetZN(state.X);
            return baseCycles;
        }

        private int TXS(Func<AddrResult> mode, int baseCycles)
        {
            state.SP = state.X;
            return baseCycles;
        }

        private int PHA(Func<AddrResult> mode, int baseCycles)
        {
            StackPush(state.A);
            return baseCycles;
        }

        private int PHP(Func<AddrResult> mode, int baseCycles)
        {
            StackPush((byte)(state.status | (1 << FLAG_B) | (1 << FLAG_UNUSED)));
            return baseCycles;
        }

        private int PLA(Func<AddrResult> mode, int baseCycles)
        {
            state.A = StackPop();
            SetZN(state.A);
            return baseCycles;
        }

        private int PLP(Func<AddrResult> mode, int baseCycles)
        {
            state.status = StackPop();
            SetFlag(FLAG_UNUSED, true);
            SetFlag(FLAG_B, false);
            return baseCycles;
        }

        // Logical
        private int AND(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A & bus.Read(addr.address));
            SetZN(state.A);

            return baseCycles + addr.extraCycles;
        }

        private int EOR(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A ^ bus.Read(addr.address));
            SetZN(state.A);

            return baseCycles + addr.extraCycles;
        }

        private int ORA(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A | bus.Read(addr.address));
            SetZN(state.A);
            return baseCycles + addr.extraCycles;
        }

        private int BIT(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);

            SetFlag(FLAG_Z, (state.A & value) == 0);
            SetFlag(FLAG_N, (value & 0x80) != 0);
            SetFlag(FLAG_V, (value & 0x40) != 0);

            return baseCycles + addr.extraCycles;
        }

        // Arithmetic
        private void AddWithCarry(byte value)
        {
            ushort sum = (ushort)(state.A + value + (GetFlag(FLAG_C) ? 1 : 0));

            if (doFudge && fudger.NextDouble() < adcFudgeChance)
            {
                bool fudge = fudger.Next(2) == 1;
                if (fudge & sum < ushort.MaxValue)
                {
                    Console.WriteLine("ADC fudge");
                    bool fudge2 = fudger.Next(2) == 1;
                    if (fudge2)
                    {
                        sum--;
                    }
                    else
                    {
                        sum++;
                    }
                }
            }

            SetFlag(FLAG_C, sum > 0xFF);
            SetFlag(FLAG_Z, (sum & 0xFF) == 0);
            SetFlag(FLAG_N, (sum & 0x80) != 0);
            SetFlag(FLAG_V, (~(state.A ^ value) & (state.A ^ sum) & 0x80) != 0);

            state.A = (byte)(sum & 0xFF);
        }

        private int ADC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            AddWithCarry(bus.Read(addr.address));
            return baseCycles + addr.extraCycles;
        }

        private void SubtractWithCarry(byte rawValue)
        {
            ushort value = (ushort)(rawValue ^ 0xFF);
            ushort sum = (ushort)(state.A + value + (GetFlag(FLAG_C) ? 1 : 0));

            SetFlag(FLAG_C, sum > 0xFF);
            SetFlag(FLAG_Z, (sum & 0xFF) == 0);
            SetFlag(FLAG_N, (sum & 0x80) != 0);
            SetFlag(FLAG_V, ((state.A ^ sum) & (value ^ sum) & 0x80) != 0);

            state.A = (byte)(sum & 0xFF);
        }

        private int SBC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            SubtractWithCarry(bus.Read(addr.address));
            return baseCycles + addr.extraCycles;
        }

        private int CPR(byte r, Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte M = bus.Read(addr.address);
            ushort temp = (ushort)(r - M);

            SetFlag(FLAG_C, r >= M);
            SetFlag(FLAG_Z, (temp & 0xFF) == 0);
            SetFlag(FLAG_N, (temp & 0x80) != 0);

            return baseCycles + addr.extraCycles;
        }

        // Increments and Decrements
        private int INC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bus.Write(addr.address, value);
            byte result = (byte)(value + 1);
            bus.Write(addr.address, result);
            SetZN(result);

            return baseCycles; //No extra cycle to add
        }

        private int ISC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bus.Write(addr.address, value);
            byte result = (byte)(value + 1);
            bus.Write(addr.address, result);

            SubtractWithCarry(result);

            return baseCycles;
        }

        private int DEC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bus.Write(addr.address, value);
            byte result = (byte)(value - 1);
            bus.Write(addr.address, result);
            SetZN(result);

            return baseCycles;
        }

        private int DCP(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bus.Write(addr.address, value);
            byte result = (byte)(value - 1);
            bus.Write(addr.address, result);

            ushort temp = (ushort)(state.A - result);
            SetFlag(FLAG_C, state.A >= result);
            SetFlag(FLAG_Z, (temp & 0xFF) == 0);
            SetFlag(FLAG_N, (temp & 0x80) != 0);

            return baseCycles;
        }

        private int INR(ref byte r, Func<AddrResult> mode, int baseCycles)
        {
            r++;
            SetZN(r);
            return baseCycles;
        }

        private int DER(ref byte r, Func<AddrResult> mode, int baseCycles)
        {
            r--;
            SetZN(r);
            return baseCycles;
        }

        // Shifts
        private int ASL(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = mode == Accumulator ? state.A : bus.Read(addr.address);
            SetFlag(FLAG_C, (value & 0x80) != 0);
            byte result = (byte)(value << 1);

            if (mode == Accumulator)
            {
                state.A = result;
            }
            else
            {
                bus.Write(addr.address, value);
                bus.Write(addr.address, result);
            }

            SetZN(result);

            return baseCycles;
        }

        private int SLO(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            SetFlag(FLAG_C, (value & 0x80) != 0);
            byte result = (byte)(value << 1);
            bus.Write(addr.address, value);
            bus.Write(addr.address, result);

            state.A = (byte)(state.A | result);
            SetZN(state.A);

            return baseCycles;
        }

        private int LSR(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = mode == Accumulator ? state.A : bus.Read(addr.address);
            SetFlag(FLAG_C, (value & 0x01) != 0);
            byte result = (byte)(value >> 1);

            if (mode == Accumulator)
            {
                state.A = result;
            }
            else
            {
                bus.Write(addr.address, value);
                bus.Write(addr.address, result);
            }

            SetZN(result);

            return baseCycles;
        }

        private int SRE(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            SetFlag(FLAG_C, (value & 0x01) != 0);
            byte result = (byte)(value >> 1);
            bus.Write(addr.address, value);
            bus.Write(addr.address, result);

            state.A = (byte)(state.A ^ result);
            SetZN(state.A);

            return baseCycles;
        }

        private int ROL(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = mode == Accumulator ? state.A : bus.Read(addr.address);
            bool oldCarry = GetFlag(FLAG_C);
            SetFlag(FLAG_C, (value & 0x80) != 0);
            byte result = (byte)((value << 1) | (oldCarry ? 1 : 0));

            if (mode == Accumulator)
            {
                state.A = result;
            }
            else
            {
                bus.Write(addr.address, value);
                bus.Write(addr.address, result);
            }

            SetZN(result);

            return baseCycles;
        }

        private int RLA(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bool oldCarry = GetFlag(FLAG_C);
            SetFlag(FLAG_C, (value & 0x80) != 0);
            byte result = (byte)((value << 1) | (oldCarry ? 1 : 0));
            bus.Write(addr.address, value);
            bus.Write(addr.address, result);

            state.A = (byte)(state.A & result);
            SetZN(state.A);

            return baseCycles;
        }

        private int ROR(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = mode == Accumulator ? state.A : bus.Read(addr.address);
            bool oldCarry = GetFlag(FLAG_C);
            SetFlag(FLAG_C, (value & 0x01) != 0);
            byte result = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));

            if (mode == Accumulator)
            {
                state.A = result;
            }
            else
            {
                bus.Write(addr.address, value);
                bus.Write(addr.address, result);
            }

            SetZN(result);

            return baseCycles;
        }

        private int RRA(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            bool oldCarry = GetFlag(FLAG_C);
            SetFlag(FLAG_C, (value & 0x01) != 0);
            byte result = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            bus.Write(addr.address, value);
            bus.Write(addr.address, result);

            AddWithCarry(result);

            return baseCycles;
        }

        // Jumps and Calls
        private int JMP(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.PC = addr.address;
            return baseCycles;
        }

        private int JSR()
        {
            byte targetLow = Fetch();

            ushort returnAddr = state.PC;
            StackPush((byte)((returnAddr >> 8) & 0xFF));
            StackPush((byte)(returnAddr & 0xFF));

            byte targetHigh = Fetch();
            ushort targetAddr = (ushort)((targetHigh << 8) | targetLow);

            state.PC = targetAddr;
            return 6;
        }

        private int RTS()
        {
            byte low = StackPop();
            byte high = StackPop();
            state.PC = (ushort)(((high << 8) | low) + 1);
            return 6;
        }

        // Branches
        private int BIF(bool condition, Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            int extra = 0;

            if (doFudge && (fudger.NextDouble() < jmpFudgeChance))
            {
                bool fudge = fudger.Next(2) == 1;
                if (fudge)
                {
                    Console.WriteLine("BIF fudge");
                    condition = !condition;
                }
            }


            if (condition)
            {
                ushort nextInstrAddr = state.PC;
                bus.Read(nextInstrAddr);

                if (addr.extraCycles > 0)
                {
                    ushort uncorrected = (ushort)((nextInstrAddr & 0xFF00) | (addr.address & 0x00FF));
                    bus.Read(uncorrected);
                }

                state.PC = addr.address;
                extra = 1 + addr.extraCycles;
            }

            return baseCycles + extra;
        }

        // Other unofficial opcodes
        private int ANC(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A & bus.Read(addr.address));
            SetZN(state.A);
            // Effectively copies N into C, as if the AND result had been
            // shifted through ASL/ROL.
            SetFlag(FLAG_C, (state.A & 0x80) != 0);

            return baseCycles;
        }

        private int ASR(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A & bus.Read(addr.address));
            SetFlag(FLAG_C, (state.A & 0x01) != 0);
            state.A = (byte)(state.A >> 1);
            SetZN(state.A);

            return baseCycles;
        }

        private int ARR(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            state.A = (byte)(state.A & bus.Read(addr.address));

            bool oldCarry = GetFlag(FLAG_C);
            state.A = (byte)((state.A >> 1) | (oldCarry ? 0x80 : 0));
            SetZN(state.A);

            // ARR's C/V come from bits 6/5 of the result, not from a normal
            // rotate/add - this is the correct (if odd-looking) real
            // hardware behavior, not an approximation.
            SetFlag(FLAG_C, (state.A & 0x40) != 0);
            SetFlag(FLAG_V, (((state.A >> 6) ^ (state.A >> 5)) & 1) != 0);

            return baseCycles;
        }

        private int AXS(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            byte andResult = (byte)(state.A & state.X);
            ushort temp = (ushort)(andResult - value);

            SetFlag(FLAG_C, andResult >= value);
            state.X = (byte)(temp & 0xFF);
            SetZN(state.X);

            return baseCycles;
        }

        private int ANE(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            const byte magic = 0xEE;
            state.A = (byte)((state.A | magic) & state.X & value);
            SetZN(state.A);

            return baseCycles;
        }

        private int LXA(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = bus.Read(addr.address);
            const byte magic = 0xEE;
            state.A = state.X = (byte)((state.A | magic) & value);
            SetZN(state.A);

            return baseCycles;
        }

        private int LAE(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            byte value = (byte)(bus.Read(addr.address) & state.SP);
            state.A = value;
            state.X = value;
            state.SP = value;
            SetZN(value);

            return baseCycles + addr.extraCycles;
        }

        //Status Flag Changes
        private int FSC(int bit, bool state, Func<AddrResult> mode, int baseCycles)
        {
            SetFlag(bit, state);
            return baseCycles;
        }

        //System Functions
        private int NOP()
        {
            bus.Read(state.PC);
            return 2;
        }

        private int NOP(Func<AddrResult> mode, int baseCycles)
        {
            var addr = mode();
            bus.Read(addr.address);
            return baseCycles + addr.extraCycles;
        }

        private int BRK()
        {
            state.PC++;

            StackPush((byte)((state.PC >> 8) & 0xFF));
            StackPush((byte)(state.PC & 0xFF));

            byte pushedStatus = (byte)(state.status | (1 << FLAG_B) | (1 << FLAG_UNUSED));
            StackPush(pushedStatus);

            SetFlag(FLAG_B, false);

            SetFlag(FLAG_I, true);

            byte lo = bus.Read(0xFFFE);
            byte hi = bus.Read(0xFFFF);
            state.PC = (ushort)((hi << 8) | lo);

            return 7;
        }

        private int RTI()
        {
            state.status = StackPop();
            SetFlag(FLAG_UNUSED, true);
            SetFlag(FLAG_B, false);

            byte low = StackPop();
            byte high = StackPop();
            state.PC = (ushort)((high << 8) | low);

            return 6;
        }

        public int IRQ()
        {
            if (GetFlag(FLAG_I) == false)
            {
                StackPush((byte)((state.PC >> 8) & 0xFF));
                StackPush((byte)(state.PC & 0xFF));

                SetFlag(FLAG_B, false);
                SetFlag(FLAG_UNUSED, true);
                StackPush(state.status);

                SetFlag(FLAG_I, true);

                byte low = bus.Read(0xFFFE);
                byte high = bus.Read(0xFFFF);
                state.PC = (ushort)((high << 8) | low);

                return 7;
            }

            return 0;
        }

        public int NMI()
        {
            StackPush((byte)((state.PC >> 8) & 0xFF));
            StackPush((byte)(state.PC & 0xFF));

            SetFlag(FLAG_B, false);
            SetFlag(FLAG_UNUSED, true);
            StackPush(state.status);

            SetFlag(FLAG_I, true);

            byte low = bus.Read(0xFFFA);
            byte high = bus.Read(0xFFFB);
            state.PC = (ushort)((high << 8) | low);

            return 7;
        }

        private struct AddrResult
        {
            public ushort address;
            public int extraCycles;

            public AddrResult(ushort addr, int extra)
            {
                address = addr;
                extraCycles = extra;
            }
        }

        private AddrResult Implied()
        {
            bus.Read(state.PC);
            return new AddrResult(0, 0);
        }

        private AddrResult Accumulator()
        {
            bus.Read(state.PC);
            return new AddrResult(0, 0);
        }

        private AddrResult Immediate()
        {
            return new AddrResult(state.PC++, 0);
        }

        private AddrResult ZeroPage()
        {
            byte addr = Fetch();
            return new AddrResult(addr, 0);
        }

        private AddrResult ZeroPageX()
        {
            byte baseAddr = Fetch();
            bus.Read(baseAddr);
            byte addr = (byte)(baseAddr + state.X);
            return new AddrResult(addr, 0);
        }

        private AddrResult ZeroPageY()
        {
            byte baseAddr = Fetch();
            bus.Read(baseAddr);
            byte addr = (byte)(baseAddr + state.Y);
            return new AddrResult(addr, 0);
        }

        private AddrResult Absolute()
        {
            ushort addr = Fetch16Bits();
            return new AddrResult(addr, 0);
        }

        private AddrResult AbsoluteX() => AbsoluteXCore(false);
        private AddrResult AbsoluteXDummyRead() => AbsoluteXCore(true);

        private AddrResult AbsoluteXCore(bool alwaysDummyRead)
        {
            ushort baseAddr = Fetch16Bits();
            ushort effective = (ushort)(baseAddr + state.X);
            bool crossed = HasPageCrossPenalty(baseAddr, effective);

            if (crossed || alwaysDummyRead)
            {
                ushort uncorrected = (ushort)((baseAddr & 0xFF00) | (effective & 0x00FF));
                bus.Read(uncorrected);
            }

            int penalty = crossed ? 1 : 0;
            return new AddrResult(effective, penalty);
        }

        private AddrResult AbsoluteY() => AbsoluteYCore(false);
        private AddrResult AbsoluteYDummyRead() => AbsoluteYCore(true);

        private AddrResult AbsoluteYCore(bool alwaysDummyRead)
        {
            ushort baseAddr = Fetch16Bits();
            ushort effective = (ushort)(baseAddr + state.Y);
            bool crossed = HasPageCrossPenalty(baseAddr, effective);

            if (crossed || alwaysDummyRead)
            {
                ushort uncorrected = (ushort)((baseAddr & 0xFF00) | (effective & 0x00FF));
                bus.Read(uncorrected);
            }

            int penalty = crossed ? 1 : 0;
            return new AddrResult(effective, penalty);
        }

        private AddrResult IndirectX()
        {
            byte zp = Fetch();
            byte ptr = (byte)(zp + state.X);
            ushort addr = (ushort)(bus.Read(ptr) | (bus.Read((byte)(ptr + 1)) << 8));
            return new AddrResult(addr, 0);
        }

        private AddrResult IndirectY() => IndirectYCore(false);
        private AddrResult IndirectYDummyRead() => IndirectYCore(true);

        private AddrResult IndirectYCore(bool alwaysDummyRead)
        {
            byte zp = Fetch();
            ushort baseAddr = (ushort)(bus.Read(zp) | (bus.Read((byte)(zp + 1)) << 8));
            ushort effective = (ushort)(baseAddr + state.Y);
            bool crossed = HasPageCrossPenalty(baseAddr, effective);

            if (crossed || alwaysDummyRead)
            {
                ushort uncorrected = (ushort)((baseAddr & 0xFF00) | (effective & 0x00FF));
                bus.Read(uncorrected);
            }

            int penalty = crossed ? 1 : 0;
            return new AddrResult(effective, penalty);
        }

        private AddrResult Indirect()
        {
            ushort ptr = Fetch16Bits();
            byte lo = bus.Read(ptr);
            byte hi = (ptr & 0x00FF) == 0x00FF ? bus.Read((ushort)(ptr & 0xFF00)) : bus.Read((ushort)(ptr + 1));
            ushort addr = (ushort)((hi << 8) | lo);
            return new AddrResult(addr, 0);
        }

        private AddrResult Relative()
        {
            sbyte offset = (sbyte)Fetch();
            ushort target = (ushort)(state.PC + offset);
            int penalty = HasPageCrossPenalty(state.PC, target) ? 1 : 0;
            return new AddrResult(target, penalty);
        }

        private bool HasPageCrossPenalty(ushort baseAddr, ushort effectiveAddr)
        {
            return (baseAddr & 0xFF00) != (effectiveAddr & 0xFF00);
        }
    }
}