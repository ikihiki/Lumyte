using Lumyte.Audio.Abstractions;
using Xunit;

namespace Lumyte.Audio.OpenAL.Tests;

/// <summary>Exercises allocation recovery against OpenAL Soft.</summary>
public sealed class OpenALRegressionTests
{
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
