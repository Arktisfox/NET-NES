using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace NES
{
    public struct DecayingByte
    {
        private const double DecayMilliseconds = 500.0;

        private byte value;
        private readonly long[] bitTimestamps;

        public DecayingByte(byte initialValue = 0)
        {
            bitTimestamps = new long[8];
            value = initialValue;
            Write(initialValue);
        }

        public void Write(byte newValue)
        {
            long now = Stopwatch.GetTimestamp();

            for (int bit = 0; bit < 8; bit++)
            {
                byte mask = (byte)(1 << bit);

                if ((newValue & mask) != 0)
                {
                    value |= mask;
                }
                else
                {
                    value &= (byte)~mask;
                }

                bitTimestamps[bit] = now;
            }
        }

        public byte Read()
        {
            long now = Stopwatch.GetTimestamp();
            byte result = value;

            for (int bit = 0; bit < 8; bit++)
            {
                long elapsed = now - bitTimestamps[bit];

                double milliseconds =
                    elapsed * 1000.0 / Stopwatch.Frequency;

                if (milliseconds >= DecayMilliseconds)
                    result &= (byte)~(1 << bit);
            }

            return result;
        }

        public static implicit operator byte(DecayingByte value) => value.Read();

        public static implicit operator DecayingByte(byte value) => new DecayingByte(value);
    }
}