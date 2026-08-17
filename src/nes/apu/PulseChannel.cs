namespace NES
{
    public struct PulseChannelState
    {
        // $4000
        public int duty;
        public bool envelopeLoop;
        public bool constantVolume;
        public int volume;

        // $4001
        public bool sweepEnabled;
        public int sweepPeriod;
        public bool sweepNegate;
        public int sweepShift;
        public bool sweepReload;
        public int sweepDivider;

        // Timer
        public int timer;
        public int timerReload;

        // Sequencer
        public int sequence;

        // Length counter
        public int lengthCounter;
        public bool enabled;

        // Envelope
        public int envelopeDivider;
        public int envelopeDecay;
        public bool envelopeStart;
    }

    public class PulseChannel : IAPUChannel
    {
        public PulseChannelState State
        {
            get => state;
            set => state = value;
        }
        private PulseChannelState state;

        private static readonly byte[,] DutyTable =
        {
            { 0, 1, 0, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 1, 1, 0, 0, 0 },
            { 1, 0, 0, 1, 1, 1, 1, 1 }
        };

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

        // Pulse 1 and pulse 2 differ in how the sweep unit negates: pulse 1 uses
        // one's complement (extra -1), pulse 2 uses two's complement.
        private readonly bool isChannel1;

        public PulseChannel(bool isChannel1)
        {
            state = new PulseChannelState();
            this.isChannel1 = isChannel1;
        }

        public bool Enabled
        {
            get => state.enabled;
            set
            {
                state.enabled = value;
                if (!state.enabled)
                    state.lengthCounter = 0;
            }
        }

        public int LengthCounter => state.lengthCounter;

        public void Write0(byte value)
        {
            state.duty = (value >> 6) & 3;

            state.envelopeLoop = (value & 0x20) != 0;
            state.constantVolume = (value & 0x10) != 0;

            state.volume = value & 0x0F;
        }

        public void Write1(byte value)
        {
            state.sweepEnabled = (value & 0x80) != 0;
            state.sweepPeriod = ((value >> 4) & 7) + 1;

            state.sweepNegate = (value & 0x08) != 0;
            state.sweepShift = value & 7;

            state.sweepReload = true;
        }

        public void Write2(byte value)
        {
            state.timerReload = (state.timerReload & 0x700) | value;
        }

        public void Write3(byte value)
        {
            state.timerReload = (state.timerReload & 0x0FF) | ((value & 7) << 8);

            // Writing $4003/$4007 reloads the length counter (if the channel is
            // enabled via $4015 - otherwise it stays silenced at 0).
            if (state.enabled)
                state.lengthCounter = LengthTable[value >> 3];

            // It also restarts the envelope.
            state.envelopeStart = true;

            // And resets the sequencer.
            state.sequence = 0;
        }

        public void ClockTimer()
        {
            if (state.timer == 0)
            {
                state.timer = state.timerReload;

                state.sequence++;
                if (state.sequence >= 8)
                    state.sequence = 0;
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

            ClockSweep();
        }

        private int SweepTarget()
        {
            int change = state.timerReload >> state.sweepShift;

            int target = state.sweepNegate
                ? state.timerReload - change - (isChannel1 ? 1 : 0)
                : state.timerReload + change;

            return target < 0 ? 0 : target;
        }

        private bool SweepMuting()
        {
            return state.timerReload < 8 || SweepTarget() > 0x7FF;
        }

        private void ClockSweep()
        {
            bool shouldUpdate = state.sweepDivider == 0 && state.sweepEnabled && state.sweepShift != 0 && !SweepMuting();

            if (shouldUpdate)
                state.timerReload = SweepTarget();

            if (state.sweepDivider == 0 || state.sweepReload)
            {
                state.sweepDivider = state.sweepPeriod;
                state.sweepReload = false;
            }
            else
            {
                state.sweepDivider--;
            }
        }

        public int Sample()
        {
            if (!state.enabled || state.lengthCounter == 0)
                return 0;

            // Sweep muting / ultrasonic periods shouldn't output.
            if (SweepMuting())
                return 0;

            if (DutyTable[state.duty, state.sequence] == 0)
                return 0;

            return state.constantVolume ? state.volume : state.envelopeDecay;
        }
    }
}