using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class SeekScenario
{
    internal static void Run(AudioModule audio, ResourceManager manager, Action<Task> wait)
    {
        Task<ResourceReference<AudioClip>> load = manager.RetainAsync(new ResourceKey<AudioClip>("seek")).AsTask();
        wait(load);
        using ResourceReference<AudioClip> reference = load.GetAwaiter().GetResult();
        using ResourceLease<AudioClip> lease = reference.Pin();
        AudioClip clip = lease.Value;
        long quarter = clip.FrameCount / 4;
        long half = clip.FrameCount / 2;
        AudioVoice first = VoiceScenario.CreateVoice(clip, wait);
        try
        {
            AudioVoice second = VoiceScenario.CreateVoice(clip, wait);
            try
            {
                wait(first.SetLoopAsync(true).AsTask());
                wait(first.SetGainAsync(0.25f).AsTask());
                wait(first.SeekAsync(quarter).AsTask());
                wait(second.SeekAsync(half).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);
                At(audio, second, AudioPlaybackState.Stopped, half, wait);

                Expect<ArgumentOutOfRangeException>(() => wait(first.SeekAsync(-1).AsTask()));
                Expect<ArgumentOutOfRangeException>(() => wait(first.SeekAsync(clip.FrameCount + 1).AsTask()));
                Expect<ArgumentOutOfRangeException>(() => wait(first.SeekAsync(long.MaxValue).AsTask()));
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);

                wait(first.PlayAsync().AsTask());
                wait(first.SeekAsync(half).AsTask());
                wait(audio.Commands.RefreshAsync().AsTask());
                Require(first.State == AudioPlaybackState.Playing, "Seeking stopped a playing voice.");
                wait(first.PauseAsync().AsTask());
                wait(first.SeekAsync(quarter).AsTask());
                At(audio, first, AudioPlaybackState.Paused, quarter, wait);
                Require(first.Loop && first.Gain == 0.25f, "Seeking changed the loop or gain settings.");
                At(audio, second, AudioPlaybackState.Stopped, half, wait);

                wait(first.StopAsync().AsTask());
                At(audio, first, AudioPlaybackState.Stopped, 0, wait);
                wait(first.SeekAsync(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, 0, wait);
                wait(first.SetLoopAsync(false).AsTask());
                wait(first.SeekAsync(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, clip.FrameCount, wait);
                wait(first.PlayAsync().AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);
                wait(first.SeekAsync(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);

                wait(first.SeekAsync(quarter).AsTask());
                At(audio, first, AudioPlaybackState.Stopped, quarter, wait);
                wait(first.SetLoopAsync(true).AsTask());
                wait(first.PlayAsync().AsTask());
                wait(first.PauseAsync().AsTask());
                wait(first.SetLoopAsync(false).AsTask());
                wait(first.SeekAsync(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Paused, clip.FrameCount, wait);
                wait(first.PlayAsync().AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);

                wait(first.SetLoopAsync(true).AsTask());
                wait(first.PlayAsync().AsTask());
                wait(audio.Commands.RefreshAsync().AsTask());
                Require(first.State == AudioPlaybackState.Playing && first.FramePosition < clip.FrameCount, "Completed playback did not restart from the beginning.");
                wait(first.SetLoopAsync(false).AsTask());
                wait(first.SeekAsync(clip.FrameCount).AsTask());
                At(audio, first, AudioPlaybackState.Completed, clip.FrameCount, wait);
                At(audio, second, AudioPlaybackState.Stopped, half, wait);
            }
            finally
            {
                wait(second.DestroyAsync().AsTask());
            }
        }
        finally
        {
            wait(first.DestroyAsync().AsTask());
        }

        Expect<ObjectDisposedException>(() => wait(first.SeekAsync(0).AsTask()));
        Expect<ObjectDisposedException>(() => _ = first.FramePosition);
        Console.WriteLine("Resource audio seek passed: owner commands, independent positions, pause/stop/loop/end boundaries, and invalid/disposed requests.");
    }

    private static void At(AudioModule audio, AudioVoice voice, AudioPlaybackState state, long position, Action<Task> wait)
    {
        wait(audio.Commands.RefreshAsync().AsTask());
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
