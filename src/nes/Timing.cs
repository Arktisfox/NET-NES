namespace NES
{
    public enum TvSystem
    {
        NTSC,
        PAL
    }

    public sealed class Timing
    {
        public TvSystem System;

        public double CpuFrequency;
        public int ScanlinesPerFrame;
        public double FrameRate;

        public int PpuCyclesNumerator;
        public int PpuCyclesDenominator;

        public int CpuCyclesPerFrame;

        public int[] APUFrameSequence4Step = Array.Empty<int>();
        public int[] APUFrameSequence5Step = Array.Empty<int>();
        public int[] DMCRateTable = Array.Empty<int>();

        private Timing()
        {

        }

        public static readonly Timing NTSC = new()
        {
            System = TvSystem.NTSC,
            CpuFrequency = 1_789_773.0,
            ScanlinesPerFrame = 262,
            FrameRate = 60.0988,
            PpuCyclesNumerator = 3,
            PpuCyclesDenominator = 1,
            CpuCyclesPerFrame = 29781,
            APUFrameSequence4Step = new[] { 7457, 14913, 22371, 29829 },
            APUFrameSequence5Step = new[] { 7457, 14913, 22371, 29829, 37281 },
            DMCRateTable = new[]
            {
                428, 380, 340, 320, 286, 254, 226, 214,
                190, 160, 142, 128, 106,  84,  72,  54
            }
        };

        public static readonly Timing PAL = new()
        {
            System = TvSystem.PAL,
            CpuFrequency = 1_662_607.0,
            ScanlinesPerFrame = 312,
            FrameRate = 50.0070,
            PpuCyclesNumerator = 16,
            PpuCyclesDenominator = 5,
            CpuCyclesPerFrame = 33248,
            APUFrameSequence4Step = new[] { 8313, 16625, 24939, 33253 },
            APUFrameSequence5Step = new[] { 8313, 16625, 24939, 33253, 41563 },
            DMCRateTable = new[]
            {
                398, 354, 316, 298, 276, 236, 210, 198,
                176, 148, 132, 118,  98,  78,  66,  50
            }
        };

        public static Timing For(TvSystem system) => system == TvSystem.PAL ? PAL : NTSC;
    }
}