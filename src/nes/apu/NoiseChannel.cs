namespace NES
{
    public struct NoiseChannelState
    {
        // $400C - the envelope loop flag doubles as the length counter halt flag.
        public bool envelopeLoop;
        public bool constantVolume;
        public int volume;

        // $400E
        public bool modeFlag;
        public int timer;
        public int timerReload;

        // 15-bit LFSR, must never be zero.
        public int shiftRegister;

        // Length counter
        public int lengthCounter;
        public bool enabled;

        // Envelope
        public int envelopeDivider;
        public int envelopeDecay;
        public bool envelopeStart;

        public NoiseChannelState()
        {
            envelopeLoop = false;
            constantVolume = false;
            volume = 0;
            modeFlag = false;
            timer = 0;
            timerReload = 0;
            shiftRegister = 1;
            lengthCounter = 0;
            enabled = false;
            envelopeDivider = 0;
            envelopeDecay = 0;
            envelopeStart = false;
        }
    }

    public class NoiseChannel : IAPUChannel
    {
        public NoiseChannelState State
        {
            get => state;
            set => state = value;
        }
        private NoiseChannelState state;

        // Noise timer period tables, in CPU cycles. NTSC and PAL differ because
        // the underlying clock rate differs. See https://www.nesdev.org/wiki/APU_Noise
        private static readonly int[] NtscPeriodTable =
        {
        4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068
    };
        private static readonly int[] PalPeriodTable =
        {
        4, 7, 14, 30, 60, 88, 118, 148, 188, 236, 354, 472, 708, 944, 1890, 3778
    };

        private int[] PeriodTable = NtscPeriodTable;

        private static readonly byte[] LengthTable =
        {
        10, 254, 20,  2,
        40,   4, 80,  6,
        160,  8, 60, 10,
        14,  12, 26, 14,
        12,  16, 24, 18,
        48,  20, 96, 22,
        192, 24, 72, 26,
        16,  28, 32, 30
    };

        public bool Enabled
        {
            get => state.enabled;
            set
            {
                state.enabled = value;
                if (!state.enabled)
                {
                    state.lengthCounter = 0;
                }
            }
        }

        public int LengthCounter => state.lengthCounter;

        public void Write0(byte value)
        {
            state.envelopeLoop = (value & 0x20) != 0;
            state.constantVolume = (value & 0x10) != 0;
            state.volume = value & 0x0F;
        }

        public void Write2(byte value)
        {
            state.modeFlag = (value & 0x80) != 0;
            state.timerReload = PeriodTable[value & 0x0F];
        }

        public void Write3(byte value)
        {
            if (state.enabled)
                state.lengthCounter = LengthTable[value >> 3];

            state.envelopeStart = true;
        }

        public void ClockTimer()
        {
            if (state.timer == 0)
            {
                state.timer = state.timerReload;

                int otherBit = state.modeFlag ? 6 : 1;
                int feedback = (state.shiftRegister ^ (state.shiftRegister >> otherBit)) & 1;

                state.shiftRegister >>= 1;
                state.shiftRegister |= feedback << 14;
            }
            else
            {
                state.timer--;
            }
        }

        public void ClockQuarterFrame()
        {
            if (state.envelopeStart)
            {
                state.envelopeStart = false;
                state.envelopeDecay = 15;
                state.envelopeDivider = state.volume;
            }
            else
            {
                if (state.envelopeDivider == 0)
                {
                    state.envelopeDivider = state.volume;

                    if (state.envelopeDecay == 0)
                    {
                        if (state.envelopeLoop)
                            state.envelopeDecay = 15;
                    }
                    else
                    {
                        state.envelopeDecay--;
                    }
                }
                else
                {
                    state.envelopeDivider--;
                }
            }
        }

        public void ClockHalfFrame()
        {
            if (!state.envelopeLoop && state.lengthCounter > 0)
                state.lengthCounter--;
        }

        public int Sample()
        {
            if (!state.enabled || state.lengthCounter == 0)
                return 0;

            // muute check
            if ((state.shiftRegister & 1) != 0)
                return 0;

            return state.constantVolume ? state.volume : state.envelopeDecay;
        }

        public NoiseChannel()
        {
            state = new NoiseChannelState
            {
                timerReload = PeriodTable[0]
            };
        }

        public void SetTvSystem(TvSystem system)
        {
            PeriodTable = system == TvSystem.PAL ? PalPeriodTable : NtscPeriodTable;
            state.timerReload = PeriodTable[0];
        }
    }
}