using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio;

internal static class AudioValidation
{
    internal static void Pcm(AudioFormat format, ReadOnlySpan<float> samples)
    {
        if (format.SampleRate <= 0 || format.Channels is not (1 or 2) || samples.IsEmpty || samples.Length % format.Channels != 0)
        {
            throw new ArgumentException("PCM must have a positive rate, one or two channels, and complete nonempty frames.");
        }

        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                throw new ArgumentException("PCM must contain finite samples.", nameof(samples));
            }
        }
    }

    internal static void Gain(float gain)
    {
        if (!float.IsFinite(gain) || gain < 0 || gain > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }
    }

    internal static void Bus(AudioBus bus, bool destination = false)
    {
        if (!Enum.IsDefined(bus) || (destination && bus == AudioBus.Master))
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
    }
}
