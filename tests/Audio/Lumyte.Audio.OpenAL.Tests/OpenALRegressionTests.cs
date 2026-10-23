using Lumyte.Audio.Abstractions;
using Xunit;

namespace Lumyte.Audio.OpenAL.Tests;

/// <summary>Exercises allocation recovery against OpenAL Soft.</summary>
public sealed class OpenALRegressionTests
{
    /// <summary>Cached getters are synchronized with owner updates without native calls on the reader.</summary>
    [OpenALFact]
    public void CachedStateCanBeReadFromAnotherThread()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[48000]);
        using IAudioVoice voice = device.CreateVoice(clip);
        Exception? failure = null;
        var reader = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    Assert.InRange(voice.Gain, 0, 1);
                    _ = voice.Loop;
                    Assert.True(Enum.IsDefined(voice.State));
                }

                Assert.Throws<InvalidOperationException>(voice.Play);
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        reader.Start();
        try
        {
            for (int i = 0; i < 100; i++)
            {
                voice.Gain = (i % 2) * 0.5f;
                voice.Loop = i % 2 == 0;
                device.Update();
            }
        }
        finally
        {
            reader.Join();
        }

        Assert.Null(failure);
        voice.Dispose();
        Assert.Throws<ObjectDisposedException>(() => voice.State);
    }

    /// <summary>Checks that a failed native allocation does not poison existing resources.</summary>
    [OpenALFact]
    public void NativeSourceExhaustionPreservesExistingVoicesAndAllowsRecovery()
    {
        using var device = new OpenALDevice(maxVoices: 4096);
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[48000]);
        var voices = new List<IAudioVoice>();
        try
        {
            InvalidOperationException? failure = null;
            for (int i = 0; i < 4096; i++)
            {
                try
                {
                    voices.Add(device.CreateVoice(clip));
                }
                catch (InvalidOperationException error)
                {
                    failure = error;
                    break;
                }
            }

            Assert.NotNull(failure);
            Assert.Contains("OutOfMemory", failure.Message, StringComparison.Ordinal);
            Assert.NotEmpty(voices);

            // This must work immediately after the allocation error, without a
            // separate API call to clear a stale native error first.
            voices[0].Dispose();
            voices.RemoveAt(0);
            using IAudioVoice replacement = device.CreateVoice(clip);
            replacement.Play();
            replacement.Pause();
            device.Update();
            Assert.Equal(AudioPlaybackState.Paused, replacement.State);

            foreach (IAudioVoice voice in voices)
            {
                voice.Gain = 0.25f;
                voice.Play();
                voice.Stop();
            }
        }
        finally
        {
            foreach (IAudioVoice voice in voices)
            {
                voice.Dispose();
            }
        }

        clip.Dispose();
        device.Dispose();
    }
}
