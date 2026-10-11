using System.Collections.Concurrent;
using Lumyte.Audio.Abstractions;
using Silk.NET.OpenAL;
using Xunit;

namespace Lumyte.Audio.OpenAL.Tests;

/// <summary>Exercises allocation recovery and context isolation against OpenAL Soft.</summary>
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

    /// <summary>Checks that disposal leaves another current device on the same thread intact.</summary>
    [OpenALFact]
    public unsafe void DevicesOnOneThreadRemainIndependentDuringDisposal()
    {
        using var api = ALContext.GetApi();
        using var first = new OpenALDevice();
        nint firstContext = (nint)api.GetCurrentContext();
        using IAudioClip clip = first.CreateClip(new(48000, 1), new float[48000]);
        using IAudioVoice voice = first.CreateVoice(clip);
        using var second = new OpenALDevice();
        Assert.NotEqual(firstContext, (nint)api.GetCurrentContext());

        first.Update();
        Assert.Equal(firstContext, (nint)api.GetCurrentContext());
        second.Dispose();
        Assert.Equal(firstContext, (nint)api.GetCurrentContext());

        voice.Play();
        voice.Pause();
        first.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        voice.Dispose();
        clip.Dispose();
        first.Dispose();
        Assert.Equal(nint.Zero, (nint)api.GetCurrentContext());
    }

    /// <summary>Checks that devices owned by separate threads keep independent current contexts.</summary>
    [OpenALFact]
    public void DevicesOnDifferentOwnerThreadsRemainIndependent()
    {
        var errors = new ConcurrentQueue<Exception>();
        using var firstReady = new ManualResetEventSlim();
        using var ready = new Barrier(2);
        Thread[] threads = Enumerable.Range(0, 2).Select(index => new Thread(() =>
        {
            try
            {
                if (index == 1 && !firstReady.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The first device did not initialize.");
                }

                using var device = new OpenALDevice();
                using var api = ALContext.GetApi();
                nint originalContext;
                unsafe
                {
                    originalContext = (nint)api.GetCurrentContext();
                }

                firstReady.Set();
                if (!ready.SignalAndWait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The second device did not initialize.");
                }

                unsafe
                {
                    Assert.Equal(originalContext, (nint)api.GetCurrentContext());
                }

                for (int i = 0; i < 128; i++)
                {
                    using IAudioClip clip = device.CreateClip(new(48000, 1), new float[48000 * (index + 1)]);
                    using IAudioVoice voice = device.CreateVoice(clip);
                    voice.Gain = 0.5f;
                    voice.Play();
                    voice.Pause();
                    device.Update();
                    Assert.Equal(AudioPlaybackState.Paused, voice.State);
                    voice.Play();
                    voice.Stop();
                }
            }
            catch (Exception error)
            {
                errors.Enqueue(error);
            }
        })
        { IsBackground = true }).ToArray();

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Audio owner thread did not complete.");
        }

        Assert.Empty(errors);
    }
}
