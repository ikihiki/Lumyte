using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class SeekScenario
{
    internal static void Run(AudioModule audio, ResourceManager manager, Action<Task> wait)
    {
        Task<ResourceReference<AudioClip>> load = manager.AcquireAsync(new ResourceKey<AudioClip>("seek")).AsTask();
        wait(load);
        using ResourceReference<AudioClip> reference = load.GetAwaiter().GetResult();
        using ResourceLease<AudioClip> lease = reference.Acquire();
        AudioClip clip = lease.Value;
        long quarter = clip.FrameCount / 4;
        long half = clip.FrameCount / 2;
        AudioVoice first = VoiceScenario.CreateVoice(clip, wait);
        try
        {
            AudioVoice second = VoiceScenario.CreateVoice(clip, wait);
            try
            {
                wait(first.SetLoop(true).AsTask());
                wait(first.SetGain(0.25f).AsTask());
                wait(first.Seek(quarter).AsTask());
                wait(second.Seek(half).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);
                At(audio, second, AudioPlaybackState.Stopped, half, wait);

                Expect<ArgumentOutOfRangeException>(() => wait(first.Seek(-1).AsTask()));
                Expect<ArgumentOutOfRangeException>(() => wait(first.Seek(clip.FrameCount + 1).AsTask()));
                Expect<ArgumentOutOfRangeException>(() => wait(first.Seek(long.MaxValue).AsTask()));
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);

                wait(first.Play().AsTask());
                wait(first.Seek(half).AsTask());
                wait(audio.Commands.Refresh().AsTask());
                Require(first.State == AudioPlaybackState.Playing, "Seeking stopped a playing voice.");
                wait(first.Pause().AsTask());
                wait(first.Seek(quarter).AsTask());
                At(audio, first, AudioPlaybackState.Paused, quarter, wait);
                Require(first.Loop && first.Gain == 0.25f, "Seeking changed the loop or gain settings.");
                At(audio, second, AudioPlaybackState.Stopped, half, wait);

                wait(first.Stop().AsTask());
                At(audio, first, AudioPlaybackState.Stopped, 0, wait);
                wait(first.Seek(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, 0, wait);
                wait(first.SetLoop(false).AsTask());
                wait(first.Seek(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, clip.FrameCount, wait);
                wait(first.Play().AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);
                wait(first.Seek(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);

                wait(first.Seek(quarter).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);
                wait(first.SetLoop(true).AsTask());
                wait(first.Play().AsTask());
                wait(first.Pause().AsTask());
                wait(first.SetLoop(false).AsTask());
                wait(first.Seek(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Paused, clip.FrameCount, wait);
                wait(first.Play().AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);

                wait(first.SetLoop(true).AsTask());
                wait(first.Play().AsTask());
                wait(audio.Commands.Refresh().AsTask());
                Require(first.State == AudioPlaybackState.Playing && first.FramePosition < clip.FrameCount, "Completed playback did not restart from the beginning.");
                wait(first.SetLoop(false).AsTask());
                wait(first.Seek(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);
                At(audio, second, AudioPlaybackState.Stopped, half, wait);
            }
            finally
            {
                wait(second.Destroy().AsTask());
            }
        }
        finally
        {
            wait(first.Destroy().AsTask());
        }

        Expect<ObjectDisposedException>(() => wait(first.Seek(0).AsTask()));
        Expect<ObjectDisposedException>(() => _ = first.FramePosition);
        Console.WriteLine("Resource audio seek passed: owner commands, independent positions, pause/stop/loop/end boundaries, and invalid/disposed requests.");
    }

    private static void At(AudioModule audio, AudioVoice voice, AudioPlaybackState state, long position, Action<Task> wait)
    {
        wait(audio.Commands.Refresh().AsTask());
        Require(voice.State == state && voice.FramePosition == position, $"Expected {state} at frame {position}; observed {voice.State} at frame {voice.FramePosition}.");
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} from the owner command.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
