namespace NES
{
    public struct TriangleChannelState
    {
        // $4008 - the control flag doubles as the length counter halt flag.
        public bool controlFlag;
        public int linearReloadValue;

        public int linearCounter;
        public bool linearReloadFlag;

        public int timer;
        public int timerReload;

        public int sequence;

        public int lengthCounter;
        public bool enabled;
    }

    public class TriangleChannel : IAPUChannel
    {
        public TriangleChannelState State
        {
            get => state;
            set => state = value;
        }
        private TriangleChannelState state;

        private static readonly byte[] SequenceTable =
        {
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
         0,  1,  2,  3,  4,  5, 6, 7, 8, 9,10,11,12,13,14,15
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
            state.controlFlag = (value & 0x80) != 0;
            state.linearReloadValue = value & 0x7F;
        }

        public void Write2(byte value)
        {
            state.timerReload = (state.timerReload & 0x700) | value;
        }

        public void Write3(byte value)
        {
            state.timerReload = (state.timerReload & 0x0FF) | ((value & 7) << 8);

            if (state.enabled)
            {
                state.lengthCounter = LengthTable[value >> 3];
            }

            // Writing $400B sets the linear counter reload flag.
            state.linearReloadFlag = true;
        }

        public void ClockTimer()
        {
            // The sequencer only advances while both the length counter and the
            // linear counter are active - this holds the last output value
            // rather than clicking/popping when silenced.
            if (state.lengthCounter == 0 || state.linearCounter == 0)
                return;

            if (state.timer == 0)
            {
                state.timer = state.timerReload;

                state.sequence++;
                if (state.sequence >= 32)
                {
                    state.sequence = 0;
                }
            }
            else
            {
                state.timer--;
            }
        }

        public void ClockQuarterFrame()
        {
            if (state.linearReloadFlag)
                state.linearCounter = state.linearReloadValue;
            else if (state.linearCounter > 0)
                state.linearCounter--;

            if (!state.controlFlag)
                state.linearReloadFlag = false;
        }

        public void ClockHalfFrame()
        {
            if (!state.controlFlag && state.lengthCounter > 0)
                state.lengthCounter--;
        }

        public int Sample()
        {
            // Ultrasonic periods (timerReload < 2) are sometimes used by games to
            // silence the channel; Mute those to prevent sharp sounds.
            if (state.timerReload < 2)
                return 0;

            return SequenceTable[state.sequence];
        }

        public TriangleChannel()
        {
            state = new TriangleChannelState();
        }
    }
}