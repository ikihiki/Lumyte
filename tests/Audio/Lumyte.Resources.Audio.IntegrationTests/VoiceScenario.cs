using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class VoiceScenario
{
    internal static void Run(AudioModule audio, ResourceManager manager, TestSource source, bool host, bool reload, Action<Task> wait)
    {
        Task<ResourceReference<AudioClip>> load = manager.RetainAsync(new ResourceKey<AudioClip>("tone")).AsTask();
        wait(load);
        using ResourceReference<AudioClip> reference = load.GetAwaiter().GetResult();
        using ResourceLease<AudioClip> lease = reference.Pin();
        AudioVoice first = CreateVoice(lease.Value, wait);
        try
        {
            AudioVoice second = CreateVoice(lease.Value, wait);
            try
            {
                wait(first.SetLoopAsync(true).AsTask());
                wait(first.SetGainAsync(0.25f).AsTask());
                wait(first.PlayAsync().AsTask());
                DeviceScenario.Refresh(audio.Devices, wait);
                Require(
                    first.State == AudioPlaybackState.Playing && second.State == AudioPlaybackState.Stopped,
                    "Voices created from one cached clip did not have independent playback state.");
                Require(
                    first.Loop && !second.Loop && first.Gain == 0.25f && second.Gain == 1,
                    "Voices created from one cached clip shared control state.");
                Require(
                    (DeviceScenario.OwnerThreadId(audio.Devices, wait) == Environment.CurrentManagedThreadId) == host,
                    "Audio execution ownership is incorrect.");
                wait(second.PlayAsync().AsTask());
                wait(second.StopAsync().AsTask());
                DeviceScenario.Refresh(audio.Devices, wait);
                Require(
                    first.State == AudioPlaybackState.Playing && second.State == AudioPlaybackState.Stopped,
                    "Controlling one voice affected another voice from the same clip.");

                if (reload)
                {
                    Reload(manager, source, reference, lease, first, wait);
                }
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

        VerifyEarlyClipRelease(manager, wait);
        Console.WriteLine("Resource audio voices passed: one cached clip, independent caller-owned voices, and early clip release cleanup retry.");
    }

    internal static AudioVoice CreateVoice(AudioClip clip, Action<Task> wait)
    {
        Task<AudioVoice> create = clip.CreateVoiceAsync(AudioBus.Effects).AsTask();
        wait(create);
        return create.GetAwaiter().GetResult();
    }

    private static void Reload(ResourceManager manager, TestSource source, ResourceReference<AudioClip> reference, ResourceLease<AudioClip> old, AudioVoice oldVoice, Action<Task> wait)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, result) =>
        {
            if (result.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        source.Set(0.5f);
        wait(published.Task);
        using ResourceLease<AudioClip> next = reference.Pin();
        Require(next.Revision != old.Revision && !next.Value.Equals(old.Value), "Reload did not publish a new clip.");
        AudioVoice nextVoice = CreateVoice(next.Value, wait);
        try
        {
            Require(nextVoice.State == AudioPlaybackState.Stopped, "Creating a voice unexpectedly restarted playback.");
            wait(oldVoice.StopAsync().AsTask());
            wait(nextVoice.PlayAsync().AsTask());
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.ReloadFailed += (_, _) => failed.TrySetResult();
            source.Set(float.NaN);
            wait(failed.Task);
            using ResourceLease<AudioClip> afterFailure = reference.Pin();
            Require(
                next.Value.Equals(afterFailure.Value) && old.IsUsable,
                "Failed reload changed the publication or released an old lease.");
            Console.WriteLine("Resource audio HotReload passed: replaced clip, retained caller-owned voices and old lease, rejected invalid decode.");
        }
        finally
        {
            wait(nextVoice.DestroyAsync().AsTask());
            source.Set(0.25f);
        }
    }

    private static void VerifyEarlyClipRelease(ResourceManager manager, Action<Task> wait)
    {
        Task<ResourceReference<AudioClip>> load = manager.RetainAsync(new ResourceKey<AudioClip>("early-release")).AsTask();
        wait(load);
        ResourceReference<AudioClip> reference = load.GetAwaiter().GetResult();
        ResourceLease<AudioClip> lease = reference.Pin();
        AudioVoice voice = CreateVoice(lease.Value, wait);
        try
        {
            // Deliberately violate the caller contract: the backend must retain a clip with a live voice.
            lease.Dispose();
            reference.Dispose();
            try
            {
                wait(manager.TrimAsync().AsTask());
                throw new InvalidOperationException("A clip was released while a caller-owned voice still used it.");
            }
            catch (AggregateException)
            {
            }

            wait(voice.PlayAsync().AsTask());
        }
        finally
        {
            wait(voice.DestroyAsync().AsTask());
            lease.Dispose();
            reference.Dispose();
        }

        wait(manager.TrimAsync().AsTask());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
