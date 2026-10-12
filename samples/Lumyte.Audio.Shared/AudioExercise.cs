using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Samples;

/// <summary>Exercises real backend objects through their common interfaces.</summary>
public static class AudioExercise
{
    /// <summary>Checks state, ownership, validation, and live-child disposal.</summary>
    /// <param name="device">The device under test, with a voice limit of two.</param>
    /// <param name="other">A distinct device of the same backend.</param>
    public static void Run(IAudioDevice device, IAudioDevice other)
    {
        Equal(device.OutputState != AudioOutputState.Unavailable, "Available output");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Reject<OperationCanceledException>(() => device.ResumeOutputAsync(cancelled.Token));
        }

        var format = new AudioFormat(48000, 1);
        Reject<ArgumentException>(() => device.CreateClip(default, [0]));
        Reject<ArgumentException>(() => device.CreateClip(format, [float.NaN]));
        Reject<ArgumentException>(() => device.CreateClip(new(48000, 2), [0]));
        Reject<ArgumentException>(() => device.CreateClip(format, []));
        Reject<ArgumentOutOfRangeException>(() => device.SetBusGain(AudioBus.Master, float.PositiveInfinity));
        using IAudioClip clip = device.CreateClip(format, Tone(48000));
        Equal(clip.FrameCount == 48000, "Frame count");
        Reject<InvalidOperationException>(device.Dispose);
        Reject<ArgumentException>(() => other.CreateVoice(clip));
        Reject<ArgumentOutOfRangeException>(() => device.CreateVoice(clip, AudioBus.Master));
        using IAudioVoice voice = device.CreateVoice(clip);
        Reject<InvalidOperationException>(clip.Dispose);
        using (IAudioVoice second = device.CreateVoice(clip, AudioBus.Music))
        {
            Reject<InvalidOperationException>(() => device.CreateVoice(clip));
            voice.Gain = 0.75f;
            device.SetBusGain(AudioBus.Master, 0.8f);
            device.SetBusGain(AudioBus.Effects, 0.5f);
            voice.Loop = true;
            Equal(voice.Loop && voice.Gain == 0.75f, "Properties");
            Reject<ArgumentOutOfRangeException>(() => voice.Gain = -1);
            voice.Play();
            Equal(voice.State == AudioPlaybackState.Stopped, "State is a snapshot");
            device.Update();
            Equal(voice.State == AudioPlaybackState.Playing, "Play");
            voice.Play();
            voice.Pause();
            device.Update();
            Equal(voice.State == AudioPlaybackState.Paused, "Pause");
            voice.Play();
            device.Update();
            Equal(voice.State == AudioPlaybackState.Playing, "Resume");
            voice.Stop();
            device.Update();
            Equal(voice.State == AudioPlaybackState.Stopped, "Stop");
            voice.Play();
            voice.Stop();
            voice.Play();
            device.Update();
            Equal(voice.State == AudioPlaybackState.Playing, "Ordered restart");
        }

        voice.Dispose();
        voice.Dispose();
        Reject<ObjectDisposedException>(voice.Play);
        clip.Dispose();
        Reject<ObjectDisposedException>(() => device.CreateVoice(clip));
    }

    /// <summary>Creates one-channel finite PCM for sample playback.</summary>
    /// <param name="frames">Positive number of frames at 48 kHz.</param>
    /// <returns>The PCM samples.</returns>
    public static float[] Tone(int frames)
    {
        float[] samples = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            samples[i] = 0.1f * MathF.Sin(2 * MathF.PI * 440 * i / 48000);
        }

        return samples;
    }

    /// <summary>Asserts a condition for executable sample verification.</summary>
    /// <param name="condition">Expected true condition.</param>
    /// <param name="message">Failure description.</param>
    public static void Equal(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Reject<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
