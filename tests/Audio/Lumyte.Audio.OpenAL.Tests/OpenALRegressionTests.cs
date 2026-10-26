using System.Reflection;

namespace Lumyte.Audio.OpenAL.Tests;

/// <summary>Exercises allocation recovery against OpenAL Soft.</summary>
public sealed class OpenALRegressionTests
{
    /// <summary>A failed device initialization does not prevent later native API loads.</summary>
    [OpenALFact]
    public void DeviceInitializationFailureAllowsLaterLoads()
    {
        Assert.Throws<InvalidOperationException>(() => new OpenALDevice("Lumyte missing output device"));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var device = new OpenALDevice();
            using IAudioClip clip = device.CreateClip(new(48000, 1), new float[48]);
            using IAudioVoice voice = device.CreateVoice(clip);
            voice.Play();
            voice.Stop();
        }
    }

    /// <summary>Rented conversion storage uploads only the requested PCM sample range.</summary>
    [OpenALFact]
    public void PooledClipConversionPreservesExactNativeBufferSize()
    {
        using var device = new OpenALDevice();
        using var api = AL.GetApi();
        foreach (int samples in new[] { 32770, 1002 })
        {
            using IAudioClip clip = device.CreateClip(new(48000, 2), new float[samples]);
            uint handle = (uint)clip.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(clip)!;
            api.GetBufferProperty(handle, GetBufferInteger.Size, out int bytes);
            Assert.Equal(AudioError.NoError, api.GetError());
            Assert.Equal(samples * sizeof(short), bytes);
            Assert.Equal(samples / 2, clip.FrameCount);
        }
    }

    /// <summary>Lost generations reject controls but their native children can still be released.</summary>
    [OpenALFact]
    public void LostDeviceRejectsControlsAndRetainsCleanup()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[480]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Play();

        // Service-observed failure must latch into the original backend without native cleanup.
        device.Invalidate();
        device.Invalidate();
        Assert.Equal(AudioOutputState.Unavailable, device.OutputState);
        Assert.Throws<AudioDeviceUnavailableException>(voice.Play);
        Assert.Throws<AudioDeviceUnavailableException>(voice.Pause);
        Assert.Throws<AudioDeviceUnavailableException>(voice.Stop);
        Assert.Throws<AudioDeviceUnavailableException>(() => voice.Seek(0));
        Assert.Throws<AudioDeviceUnavailableException>(() => voice.Gain = 0.5f);
        Assert.Throws<AudioDeviceUnavailableException>(() => voice.Loop = true);
        Assert.Throws<AudioDeviceUnavailableException>(() => device.CreateVoice(clip));
        Assert.Throws<AudioDeviceUnavailableException>(() => device.CreateClip(new(48000, 1), new float[48]));
        Assert.Throws<AudioDeviceUnavailableException>(device.Update);
        Assert.Throws<AudioDeviceUnavailableException>(() => { _ = device.ResumeOutputAsync(); });
        Assert.Throws<InvalidOperationException>(clip.Dispose);
        Assert.Throws<InvalidOperationException>(device.Dispose);

        // Deletion still targets the original context after another device becomes current.
        using var replacement = new OpenALDevice();
        using IAudioClip replacementClip = replacement.CreateClip(new(48000, 1), new float[48]);
        using IAudioVoice replacementVoice = replacement.CreateVoice(replacementClip);
        voice.Dispose();
        clip.Dispose();
        device.Dispose();
        replacementVoice.Play();
        replacement.Update();
        Assert.Equal(AudioOutputState.Running, replacement.OutputState);
    }

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
                    Assert.InRange(voice.FramePosition, 0, clip.FrameCount);
                }

                Assert.Throws<InvalidOperationException>(voice.Play);
                Assert.Throws<InvalidOperationException>(() => voice.Seek(0));
                Assert.Throws<InvalidOperationException>(device.Invalidate);
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
        Assert.Throws<ObjectDisposedException>(() => voice.FramePosition);
        Assert.Throws<ObjectDisposedException>(() => voice.Seek(0));
    }

    /// <summary>Stopped and paused seeking selects exact sample frames without starting playback or publishing early.</summary>
    [OpenALFact]
    public void SeekPreservesStoppedAndPausedPlaybackAndPublishesOnUpdate()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 2), new float[960000]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Seek(12345);
        Assert.Equal(0, voice.FramePosition);
        device.Update();
        Assert.Equal(12345, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Stopped, voice.State);

        voice.Play();
        voice.Pause();
        device.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        Assert.InRange(voice.FramePosition, 12345, clip.FrameCount - 1);
        long before = voice.FramePosition;
        voice.Seek(23456);
        Assert.Equal(before, voice.FramePosition);
        device.Update();
        Assert.Equal(23456, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Paused, voice.State);

        using var api = AL.GetApi();
        uint handle = (uint)voice.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
        api.GetSourceProperty(handle, GetSourceInteger.SampleOffset, out int nativeFrame);
        Assert.Equal(AudioError.NoError, api.GetError());
        Assert.Equal(23456, nativeFrame);
        voice.Play();
        voice.Pause();
        device.Update();
        Assert.InRange(voice.FramePosition, 23456, clip.FrameCount - 1);

        voice.Stop();
        device.Update();
        Assert.Equal(0, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Stopped, voice.State);
    }

    /// <summary>Live seeking preserves playback and invalid positions leave the selected state and frame untouched.</summary>
    [OpenALFact]
    public void SeekPreservesPlayingStateAndRejectsInvalidFrames()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[480000]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Gain = 0.25f;
        voice.Play();
        voice.Seek(48000);
        device.Update();
        Assert.Equal(AudioPlaybackState.Playing, voice.State);
        Assert.InRange(voice.FramePosition, 48000, clip.FrameCount - 1);
        Assert.Equal(0.25f, voice.Gain);

        voice.Pause();
        voice.Seek(45678);
        device.Update();
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Seek(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Seek(clip.FrameCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Seek(long.MaxValue));
        device.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        Assert.Equal(45678, voice.FramePosition);
    }

    /// <summary>The endpoint has deterministic logical state even though OpenAL cannot seek to its native buffer end.</summary>
    [OpenALFact]
    public void SeekEndpointAndCompletedPlaybackHaveConsistentRestartBehavior()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[480000]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(clip.FrameCount, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Stopped, voice.State);
        voice.Play();
        device.Update();
        Assert.Equal(clip.FrameCount, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Completed, voice.State);
        voice.Play();
        voice.Pause();
        device.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        Assert.InRange(voice.FramePosition, 0, clip.FrameCount - 1);

        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        Assert.Equal(clip.FrameCount, voice.FramePosition);
        voice.Seek(32123);
        device.Update();
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        Assert.Equal(32123, voice.FramePosition);
        voice.Play();
        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(AudioPlaybackState.Completed, voice.State);
        Assert.Equal(clip.FrameCount, voice.FramePosition);
        voice.Seek(43234);
        device.Update();
        Assert.Equal(AudioPlaybackState.Stopped, voice.State);
        Assert.Equal(43234, voice.FramePosition);
        voice.Play();
        voice.Pause();
        device.Update();
        Assert.InRange(voice.FramePosition, 43234, clip.FrameCount - 1);
    }

    /// <summary>A looping voice normalizes an explicit end position to the first frame without stopping.</summary>
    [OpenALFact]
    public void LoopingSeekWrapsTheEndpointWithoutChangingPlaybackState()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[480000]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Loop = true;
        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(0, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Stopped, voice.State);
        voice.Play();
        voice.Pause();
        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(0, voice.FramePosition);
        Assert.Equal(AudioPlaybackState.Paused, voice.State);
        voice.Play();
        voice.Seek(clip.FrameCount);
        device.Update();
        Assert.Equal(AudioPlaybackState.Playing, voice.State);
        Assert.InRange(voice.FramePosition, 0, clip.FrameCount - 1);
    }

    /// <summary>Native completion reports the full clip length instead of OpenAL's reset sample offset.</summary>
    [OpenALFact]
    public void NaturalCompletionPublishesTheClipFrameCount()
    {
        using var device = new OpenALDevice();
        using IAudioClip clip = device.CreateClip(new(48000, 1), new float[48]);
        using IAudioVoice voice = device.CreateVoice(clip);
        voice.Play();
        bool completed = SpinWait.SpinUntil(
            () =>
            {
                device.Update();
                return voice.State == AudioPlaybackState.Completed;
            },
            TimeSpan.FromSeconds(2));
        Assert.True(completed);
        Assert.Equal(clip.FrameCount, voice.FramePosition);
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
