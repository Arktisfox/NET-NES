namespace NES
{
    public struct DmcChannelState
    {
        // $4010
        public bool irqEnable;
        public bool loop;
        public int timerReload;
        public int timer;

        // $4011
        public int outputLevel;

        // $4012 / $4013
        public ushort sampleAddress;
        public ushort sampleLength;

        // Memory reader state.
        public ushort currentAddress;
        public int bytesRemaining;
        public byte sampleBuffer;
        public bool sampleBufferFilled;

        // Output unit state.
        public int shiftRegister;
        public int bitsRemaining;
        public bool silence;

        public bool irqFlag;

        public DmcChannelState()
        {
            irqEnable = false;
            loop = false;
            timerReload = 0;
            timer = 0;

            outputLevel = 0;

            sampleAddress = 0;
            sampleLength = 1;

            currentAddress = 0;
            bytesRemaining = 0;
            sampleBuffer = 0;
            sampleBufferFilled = false;

            shiftRegister = 0;
            bitsRemaining = 8;
            silence = true;

            irqFlag = false;
        }
    }

    public class DmcChannel : IAPUChannel
    {
        public DmcChannelState State
        {
            get
            {
                state.irqFlag = IrqFlag;
                return state;
            }
            set
            {
                state = value;
                IrqFlag = value.irqFlag;
            }
        }
        private DmcChannelState state;

        public bool NeedsFetch => fetchPending && !state.sampleBufferFilled;
        public ushort FetchAddress => state.currentAddress;
        private bool fetchPending;

        private readonly int[] RateTable;

        private readonly Bus bus;

        public bool IrqFlag { get; private set; }
        public int BytesRemaining => state.bytesRemaining;

        public void Write0(byte value)
        {
            state.irqEnable = (value & 0x80) != 0;
            state.loop = (value & 0x40) != 0;
            state.timerReload = RateTable[value & 0x0F];

            if (!state.irqEnable)
                IrqFlag = false;
        }

        public void Write1(byte value)
        {
            state.outputLevel = value & 0x7F;
        }

        public void Write2(byte value)
        {
            // Sample addresses are $C000-$FFC0, in 64-byte steps.
            state.sampleAddress = (ushort)(0xC000 + (value * 64));
        }

        public void Write3(byte value)
        {
            // Sample lengths are 1-4081 bytes, in 16-byte steps (+1).
            state.sampleLength = (ushort)((value * 16) + 1);
        }

        public void SetEnabled(bool enable)
        {
            if (!enable)
            {
                state.bytesRemaining = 0;
            }
            else if (state.bytesRemaining == 0)
            {
                state.currentAddress = state.sampleAddress;
                state.bytesRemaining = state.sampleLength;
            }
        }

        public void ClearIrq()
        {
            IrqFlag = false;
        }

        public void ClockTimer()
        {
            RequestFetchIfNeeded();

            if (state.timer == 0)
            {
                state.timer = state.timerReload;
                ClockOutputUnit();
            }
            else
            {
                state.timer--;
            }
        }

        private void RequestFetchIfNeeded()
        {
            if (state.sampleBufferFilled || state.bytesRemaining == 0 || fetchPending)
                return;
            fetchPending = true;
        }

        public void CompleteFetch(byte value)
        {
            state.sampleBuffer = value;
            state.sampleBufferFilled = true;
            fetchPending = false;

            state.currentAddress++;
            if (state.currentAddress == 0)
                state.currentAddress = 0x8000;

            state.bytesRemaining--;

            if (state.bytesRemaining == 0)
            {
                if (state.loop)
                {
                    state.currentAddress = state.sampleAddress;
                    state.bytesRemaining = state.sampleLength;
                }
                else if (state.irqEnable)
                {
                    IrqFlag = true;
                }
            }
        }

        private void ClockOutputUnit()
        {
            if (!state.silence)
            {
                if ((state.shiftRegister & 1) != 0)
                {
                    if (state.outputLevel <= 125)
                        state.outputLevel += 2;
                }
                else
                {
                    if (state.outputLevel >= 2)
                        state.outputLevel -= 2;
                }
            }

            state.shiftRegister >>= 1;
            state.bitsRemaining--;

            if (state.bitsRemaining == 0)
            {
                state.bitsRemaining = 8;

                if (!state.sampleBufferFilled)
                {
                    state.silence = true;
                }
                else
                {
                    state.silence = false;
                    state.shiftRegister = state.sampleBuffer;
                    state.sampleBufferFilled = false;
                }
            }
        }

        /// <summary>
        /// Raw DAC output for this channel (0-127).
        /// </summary>
        public int Sample() => state.outputLevel;

        public DmcChannel(Bus bus, TvSystem tvSystem)
        {
            this.bus = bus;
            state = new DmcChannelState();
            RateTable = Timing.For(tvSystem).DMCRateTable;
            state.timerReload = RateTable[0];
        }
    }
}