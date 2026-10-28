using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class DeviceScenario
{
    internal static AudioDeviceLease Acquire(IAudioDeviceService service, Action<Task> wait)
    {
        Task<AudioDeviceLease> acquire = service.AcquireAsync().AsTask();
        wait(acquire);
        return acquire.Result;
    }

    internal static int OwnerThreadId(IAudioDeviceService service, Action<Task> wait)
    {
        using AudioDeviceLease lease = Acquire(service, wait);
        return lease.Device.OwnerThreadId;
    }

    internal static void Refresh(IAudioDeviceService service, Action<Task> wait)
    {
        try
        {
            using AudioDeviceLease lease = Acquire(service, wait);
            wait(lease.Device.RefreshAsync().AsTask());
        }
        catch (AudioDeviceUnavailableException)
        {
            // Acquisition polls availability on the owner, including before it rejects loss.
        }
    }

    internal static void Run(IAudioDeviceService service, TestFactory factory, Action<Task> wait)
    {
        using AudioDeviceLease lease = Acquire(service, wait);
        AudioDevice device = lease.Device;
        TestDevice original = factory.Current;
        int clips = original.CreatedClips;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Expect<OperationCanceledException>(() => wait(device.CreateClipAsync(new(48000, 1), new float[48], cancellation.Token).AsTask()));
        Require(original.CreatedClips == clips, "Canceled device creation reached the backend.");

        Task<AudioClip> create = device.CreateClipAsync(new(48000, 1), new float[48]).AsTask();
        wait(create);
        AudioClip clip = create.Result;
        AudioVoice voice = VoiceScenario.CreateVoice(clip, wait);
        try
        {
            wait(device.SetBusGainAsync(AudioBus.Effects, 0.5f).AsTask());
            wait(voice.PlayAsync().AsTask());
            wait(device.RefreshAsync().AsTask());
            Require(voice.State == AudioPlaybackState.Playing, "A service-resolved device did not dispatch voice updates.");

            original.SetOutput(AudioOutputState.Unavailable);
            wait(service.RecoverAsync().AsTask());
            Require(!lease.IsUsable, "Recovery left the original device lease usable.");
            Expect<AudioDeviceUnavailableException>(() => _ = lease.Device);
            Expect<AudioDeviceUnavailableException>(() => wait(device.CreateClipAsync(new(48000, 1), new float[48]).AsTask()));
            Expect<AudioDeviceUnavailableException>(() => wait(device.SetBusGainAsync(AudioBus.Effects, 1).AsTask()));
            using AudioDeviceLease replacement = Acquire(service, wait);
            Require(replacement.State.Epoch != lease.State.Epoch, "Service acquisition did not resolve the replacement device.");
            Task<int> retained = service.RetireAsync().AsTask();
            wait(retained);
            Require(retained.Result == 0 && !original.IsDisposed, "Recovery disposed a retained device generation.");
        }
        finally
        {
            wait(voice.DestroyAsync().AsTask());
            wait(clip.DestroyClipAsync().AsTask());
            lease.Dispose();
        }

        Expect<ObjectDisposedException>(() => _ = lease.Device);
        Expect<ObjectDisposedException>(() => wait(device.CreateClipAsync(new(48000, 1), new float[48]).AsTask()));
        Expect<ObjectDisposedException>(() => wait(device.RefreshAsync().AsTask()));
        wait(service.RetireAsync().AsTask());
        Require(original.IsDisposed, "Returning the device lease did not allow native retirement.");
        Console.WriteLine("Audio service device passed: owner dispatch, cancellation, exact generation, recovery and disposed lease rejection.");
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
