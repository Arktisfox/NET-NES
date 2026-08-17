using Raylib_cs;

namespace Emulator
{
    public sealed class Audio : IDisposable
    {
        public double Volume
        {
            get => volume;
            set
            {
                volume = value;
                if (streamCreated)
                {
                    Raylib.SetAudioStreamVolume(stream, (float)volume);
                }
            }
        }

        private const int SampleRate = 44100;
        private const int StreamBufferSize = 2048;

        private AudioStream stream;
        private bool streamCreated = false;
        private double volume = 1.0;

        private readonly short[] outputBuffer = new short[StreamBufferSize];
        private readonly Queue<short> samples = new();

        private const int MaxBufferedSamples = SampleRate / 10;

        public Audio()
        {
            Raylib.SetAudioStreamBufferSizeDefault(StreamBufferSize);

            stream = Raylib.LoadAudioStream(
                SampleRate,
                16,
                1
            );
            Raylib.SetAudioStreamVolume(stream, (float)volume);
            Raylib.PlayAudioStream(stream);
            streamCreated = true;
        }

        public void Reset()
        {
            samples.Clear();
        }

        public void AddSample(float sample)
        {
            // cap latency
            if (samples.Count >= MaxBufferedSamples)
            {
                return;
            }

            sample = Math.Clamp(sample, -1.0f, 1.0f);
            samples.Enqueue(
                (short)(sample * short.MaxValue)
            );
        }

        public void AddSamples(float[] samples)
        {
            foreach (float sample in samples)
                AddSample(sample);
        }

        public void Update()
        {
            if (!Raylib.IsAudioStreamProcessed(stream))
            {
                return;
            }
            if (samples.Count < StreamBufferSize)
            {
                return;
            }

            for (int i = 0; i < outputBuffer.Length; i++)
            {
                outputBuffer[i] = samples.Dequeue();
            }
            unsafe
            {
                fixed (short* ptr = outputBuffer)
                {
                    Raylib.UpdateAudioStream(
                        stream,
                        ptr,
                        outputBuffer.Length
                    );
                }
            }
        }

        public void Dispose()
        {
            Raylib.StopAudioStream(stream);
            Raylib.UnloadAudioStream(stream);
        }
    }
}