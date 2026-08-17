namespace NES
{
    [Flags]
    public enum APUChannels
    {
        Pulse1 = 1 << 0,
        Pulse2 = 1 << 1,
        Triangle = 1 << 2,
        Noise = 1 << 3,
        Dmc = 1 << 4,
        All = Pulse1 | Pulse2 | Triangle | Noise | Dmc
    }

    public struct APUState
    {
        public PulseChannelState pulse1;
        public PulseChannelState pulse2;
        public TriangleChannelState triangle;
        public NoiseChannelState noise;
        public DmcChannelState dmc;

        public int cpuCycle;
        public double sampleTimer;

        public int frameCycle;
        public int frameStep;
        public bool fiveStepMode;
        public bool frameIrqInhibit;
        public bool frameIrqFlag;

        public APUState()
        {
            // channel states are not managed directly here so just default
            pulse1 = default;
            pulse2 = default;
            triangle = default;
            noise = default;
            dmc = default;

            cpuCycle = 0;
            sampleTimer = 0;

            frameCycle = 0;
            frameStep = 0;
            fiveStepMode = false;
            frameIrqInhibit = true;
            frameIrqFlag = false;
        }
    }

    public class APU
    {
        public bool EnableIRQ = true;
        public APUChannels EnabledSoundChannels = APUChannels.All;

        public APUState State
        {
            get
            {
                state.pulse1 = pulse1.State;
                state.pulse2 = pulse2.State;
                state.triangle = triangle.State;
                state.noise = noise.State;
                state.dmc = dmc.State;

                return state;
            }
            set
            {
                state = value;

                pulse1.State = value.pulse1;
                pulse2.State = value.pulse2;
                triangle.State = value.triangle;
                noise.State = value.noise;
                dmc.State = value.dmc;

                samples.Clear();
            }
        }
        private APUState state = new APUState();

        public bool DMCFetchPending => dmc.NeedsFetch;
        public ushort DMCFetchAddress => dmc.FetchAddress;

        private readonly List<float> samples = new List<float>();

        private PulseChannel pulse1 = new PulseChannel(true);
        private PulseChannel pulse2 = new PulseChannel(false);
        private TriangleChannel triangle = new TriangleChannel();
        private NoiseChannel noise = new NoiseChannel();
        private readonly DmcChannel dmc;

        private double cpuFrequency = 1789773.0;
        private double sampleRate = 44100.0;

        private readonly int[] frameSequence4Step;
        private readonly int[] frameSequence5Step;

        private readonly Bus bus;

        public void Reset()
        {
            pulse1.Enabled = false;
            pulse2.Enabled = false;
            triangle.Enabled = false;
            noise.Enabled = false;
            dmc.SetEnabled(false);
            dmc.ClearIrq();

            state.frameIrqFlag = false;
            state.frameCycle = 0;
            state.frameStep = 0;

            samples.Clear();
        }

        public void Write(ushort address, byte value)
        {
            switch (address)
            {
                case 0x4000: pulse1.Write0(value); break;
                case 0x4001: pulse1.Write1(value); break;
                case 0x4002: pulse1.Write2(value); break;
                case 0x4003: pulse1.Write3(value); break;

                case 0x4004: pulse2.Write0(value); break;
                case 0x4005: pulse2.Write1(value); break;
                case 0x4006: pulse2.Write2(value); break;
                case 0x4007: pulse2.Write3(value); break;

                case 0x4008: triangle.Write0(value); break;
                case 0x400A: triangle.Write2(value); break;
                case 0x400B: triangle.Write3(value); break;

                case 0x400C: noise.Write0(value); break;
                case 0x400E: noise.Write2(value); break;
                case 0x400F: noise.Write3(value); break;

                case 0x4010: dmc.Write0(value); break;
                case 0x4011: dmc.Write1(value); break;
                case 0x4012: dmc.Write2(value); break;
                case 0x4013: dmc.Write3(value); break;

                case 0x4015:
                    pulse1.Enabled = (value & 0x01) != 0;
                    pulse2.Enabled = (value & 0x02) != 0;
                    triangle.Enabled = (value & 0x04) != 0;
                    noise.Enabled = (value & 0x08) != 0;
                    dmc.SetEnabled((value & 0x10) != 0);

                    // Any write to $4015 clears the DMC interrupt flag.
                    dmc.ClearIrq();
                    break;

                case 0x4017:
                    state.fiveStepMode = (value & 0x80) != 0;
                    state.frameIrqInhibit = (value & 0x40) != 0;

                    if (state.frameIrqInhibit)
                        state.frameIrqFlag = false;

                    // Writing $4017 resets the frame sequencer's divider.
                    state.frameCycle = 0;
                    state.frameStep = 0;

                    // Selecting 5-step mode immediately clocks a quarter and
                    // half frame.
                    if (state.fiveStepMode)
                    {
                        ClockQuarterFrame();
                        ClockHalfFrame();
                    }
                    break;
            }
        }

        public byte ReadStatus()
        {
            byte result = 0;

            if (pulse1.LengthCounter > 0) result |= 0x01;
            if (pulse2.LengthCounter > 0) result |= 0x02;
            if (triangle.LengthCounter > 0) result |= 0x04;
            if (noise.LengthCounter > 0) result |= 0x08;
            if (dmc.BytesRemaining > 0) result |= 0x10;
            if (state.frameIrqFlag) result |= 0x40;
            if (dmc.IrqFlag) result |= 0x80;

            // Reading $4015 clears the frame interrupt flag.
            state.frameIrqFlag = false;

            return result;
        }

        public void DoDMCFetch(byte value)
        {
            if(!DMCFetchPending)
            {
                throw new InvalidOperationException("DoDMCFetch should only be called when a fetch is pending");
            }
            dmc.CompleteFetch(value);
        }

        public void Step(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                state.cpuCycle++;

                // The triangle and DMC channels clock at the full CPU rate;
                // pulse and noise clock at half rate (one APU cycle).
                triangle.ClockTimer();
                dmc.ClockTimer();

                if ((state.cpuCycle & 1) == 0)
                {
                    pulse1.ClockTimer();
                    pulse2.ClockTimer();
                    noise.ClockTimer();
                }

                StepFrameSequencer();

                if ((state.frameIrqFlag || dmc.IrqFlag) && EnableIRQ)
                {
                    bus.CPU.RequestIRQ(true);
                }

                state.sampleTimer += 1.0;
                if (state.sampleTimer >= cpuFrequency / sampleRate)
                {
                    state.sampleTimer -= cpuFrequency / sampleRate;
                    samples.Add(GenerateSample());
                }
            }
        }

        private void StepFrameSequencer()
        {
            int[] sequence = state.fiveStepMode ? frameSequence5Step : frameSequence4Step;

            state.frameCycle++;

            if (state.frameCycle < sequence[state.frameStep])
                return;

            // In 5-step mode, step index 3 (the 4th step) does nothing.
            bool skipThisStep = (state.fiveStepMode && state.frameStep == 3);

            if (!skipThisStep)
            {
                ClockQuarterFrame();

                bool isHalfFrameStep = state.fiveStepMode
                    ? state.frameStep == 1 || state.frameStep == 4
                    : state.frameStep == 1 || state.frameStep == 3;

                if (isHalfFrameStep)
                    ClockHalfFrame();
            }

            if (!state.fiveStepMode && state.frameStep == 3 && !state.frameIrqInhibit)
                state.frameIrqFlag = true;

            bool isLastStep = (state.frameStep == sequence.Length - 1);
            state.frameStep++;

            if (isLastStep)
            {
                state.frameStep = 0;
                state.frameCycle = 0;
            }
        }

        private void ClockQuarterFrame()
        {
            pulse1.ClockQuarterFrame();
            pulse2.ClockQuarterFrame();
            triangle.ClockQuarterFrame();
            noise.ClockQuarterFrame();
        }

        private void ClockHalfFrame()
        {
            pulse1.ClockHalfFrame();
            pulse2.ClockHalfFrame();
            triangle.ClockHalfFrame();
            noise.ClockHalfFrame();
        }

        public float GenerateSample()
        {
            int p1 = (EnabledSoundChannels.HasFlag(APUChannels.Pulse1)) ? pulse1.Sample() : 0;
            int p2 = (EnabledSoundChannels.HasFlag(APUChannels.Pulse2)) ? pulse2.Sample() : 0;
            int tri = (EnabledSoundChannels.HasFlag(APUChannels.Triangle)) ? triangle.Sample() : 0;
            int noise = (EnabledSoundChannels.HasFlag(APUChannels.Noise)) ? this.noise.Sample() : 0;
            int dmc = (EnabledSoundChannels.HasFlag(APUChannels.Dmc)) ? this.dmc.Sample() : 0;

            // Standard NES non-linear DAC mixing formulas
            // (see https://www.nesdev.org/wiki/APU_Mixer).
            float pulseOut = 0f;
            if (p1 + p2 > 0)
                pulseOut = 95.88f / (8128f / (p1 + p2) + 100f);

            float tndOut = 0f;
            float tndSum = tri / 8227f + noise / 12241f + dmc / 22638f;
            if (tndSum > 0f)
                tndOut = 159.79f / (1f / tndSum + 100f);

            return pulseOut + tndOut;
        }

        public float[] GetSamples()
        {
            float[] result = samples.ToArray();
            samples.Clear();
            return result;
        }

        public APU(Bus bus, TvSystem tvSystem)
        {
            this.bus = bus;

            var timing = Timing.For(tvSystem);
            frameSequence4Step = timing.APUFrameSequence4Step;
            frameSequence5Step = timing.APUFrameSequence5Step;
            cpuFrequency = timing.CpuFrequency;

            noise.SetTvSystem(tvSystem);
            dmc = new DmcChannel(bus, tvSystem);

            Console.WriteLine("APU init");
        }
    }
}