using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class ApiScenario
{
    internal static void Run(AudioModule audio, ResourceRuntime executor, AudioResourceRuntime runtime, TestFactory factory, Action<Task> wait)
    {
        ReadyRecovery(audio, factory, wait);
        AcquireGeneration(audio, executor, runtime, factory, wait);
        DefaultVoiceBus(audio, factory, wait);
        Console.WriteLine("Audio API consistency passed: Ready recovery, shared cancellation, runtime acquisition and default voice bus.");
    }

    private static void ReadyRecovery(AudioModule audio, TestFactory factory, Action<Task> wait)
    {
        AudioDeviceSnapshot initial = audio.Devices.State;
        int creations = factory.CreationCount;
        int notifications = 0;
        void Changed(object? sender, EventArgs args) => Interlocked.Increment(ref notifications);
        audio.Devices.StateChanged += Changed;
        try
        {
            Task<AudioDeviceSnapshot[]> ready = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => await audio.Devices.RecoverAsync())));
            wait(ready);
            Require(ready.Result.All(state => state == initial), "Concurrent Ready requests changed the snapshot.");
            Require(factory.CreationCount == creations && notifications == 0, "Ready recovery created a device or published a state change.");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            ExpectCanceled(() => wait(audio.Devices.RecoverAsync(canceled.Token).AsTask()));

            TestDevice old = factory.Current;
            PendingDeviceCreation pending = factory.DelayNextCreation();
            old.SetOutput(AudioOutputState.Unavailable);

            // Do not Refresh first: recovery must check availability on the owner itself.
            using var cancellation = new CancellationTokenSource();
            Task<AudioDeviceSnapshot> canceling = audio.Devices.RecoverAsync(cancellation.Token).AsTask();
            Task<AudioDeviceSnapshot> shared = audio.Devices.RecoverAsync().AsTask();
            wait(pending.Started.Task);
            cancellation.Cancel();
            ExpectCanceled(() => wait(canceling));
            Require(!shared.IsCompleted, "Canceling one waiter abandoned the shared factory operation.");
            pending.Complete();
            wait(shared);
            AudioDeviceSnapshot recovered = shared.Result;
            Require(recovered.Status == AudioDeviceServiceStatus.Ready && recovered.Epoch == initial.Epoch + 1, "Shared recovery did not publish one new generation.");
            Require(factory.CreationCount == creations + 1, "Concurrent recovery invoked the factory more than once.");
            int changes = notifications;
            Task<AudioDeviceSnapshot> repeated = audio.Devices.RecoverAsync().AsTask();
            wait(repeated);
            Require(repeated.Result == recovered && notifications == changes, "A repeated completed recovery changed Ready state.");
            wait(audio.Devices.RetireAsync().AsTask());
            Require(old.IsDisposed, "The old unused device was retained after recovery.");
        }
        finally
        {
            audio.Devices.StateChanged -= Changed;
        }
    }

    private static void AcquireGeneration(AudioModule audio, ResourceRuntime executor, AudioResourceRuntime runtime, TestFactory factory, Action<Task> wait)
    {
        var stage = new GenerationStage();
        var memory = new MemoryManager();
        var manager = new ResourceManager("audio-api-generation", new UnusedSource(), [stage], memory, executor, stageRuntimes: [new(typeof(AudioDeviceSnapshot), runtime)]);
        try
        {
            Task<ResourceReference<AudioDeviceSnapshot>> load = manager.RetainAsync(new ResourceKey<AudioDeviceSnapshot>("generation")).AsTask();
            wait(load);
            ResourceReference<AudioDeviceSnapshot> reference = load.Result;
            ResourceLease<AudioDeviceSnapshot> resource = reference.Pin();
            TestDevice old = factory.Current;
            AudioDeviceLease acquired = stage.Lease ?? throw new InvalidOperationException("The stage did not acquire a generation.");
            Require(acquired.IsUsable && resource.Value == acquired.State, "The selected runtime did not acquire the current generation.");
            old.SetOutput(AudioOutputState.Unavailable);
            wait(audio.Devices.RecoverAsync().AsTask());
            Require(!resource.IsUsable && !acquired.IsUsable, "The resource did not track its acquired audio generation.");
            Task<int> retained = audio.Devices.RetireAsync().AsTask();
            wait(retained);
            Require(retained.Result == 0 && !old.IsDisposed, "The manager did not retain the borrowed generation lease.");
            resource.Dispose();
            reference.Dispose();
            wait(manager.TrimAsync().AsTask());
            wait(audio.Devices.RetireAsync().AsTask());
            Require(old.IsDisposed, "Manager cleanup did not return the acquired generation lease.");
        }
        finally
        {
            wait(manager.DisposeAsync().AsTask());
            wait(memory.DisposeAsync().AsTask());
        }

        var other = new AudioResourceRuntime(audio.Devices, executor);
        var invalidMemory = new MemoryManager();
        var invalidManager = new ResourceManager("audio-api-wrong-runtime", new UnusedSource(), [new GenerationStage(other)], invalidMemory, executor, stageRuntimes: [new(typeof(AudioDeviceSnapshot), runtime)]);
        try
        {
            try
            {
                wait(invalidManager.RetainAsync(new ResourceKey<AudioDeviceSnapshot>("wrong-runtime")).AsTask());
                throw new InvalidOperationException("A different runtime accepted the stage context.");
            }
            catch (InvalidOperationException error) when (error.Message == "Use the audio runtime selected for this stage.")
            {
            }
        }
        finally
        {
            wait(invalidManager.DisposeAsync().AsTask());
            wait(invalidMemory.DisposeAsync().AsTask());
        }
    }

    private static void DefaultVoiceBus(AudioModule audio, TestFactory factory, Action<Task> wait)
    {
        using AudioDeviceLease lease = DeviceScenario.Acquire(audio.Devices, wait);
        Task<AudioClip> create = lease.Device.CreateClipAsync(new(48000, 1), new float[48]).AsTask();
        wait(create);
        AudioClip clip = create.Result;
        try
        {
            Task<AudioVoice> voice = clip.CreateVoiceAsync().AsTask();
            wait(voice);
            Require(factory.Current.LastVoiceBus == AudioBus.Effects, "An omitted bus did not select Effects.");
            wait(voice.Result.DestroyAsync().AsTask());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            ExpectCanceled(() => wait(clip.CreateVoiceAsync(canceled.Token).AsTask()));
        }
        finally
        {
            wait(clip.DestroyClipAsync().AsTask());
        }
    }

    private static void ExpectCanceled(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("A canceled API request completed successfully.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class GenerationStage(AudioResourceRuntime? other = null) : IResourceSourceStage<AudioDeviceSnapshot, AudioResourceRuntime>
    {
        internal AudioDeviceLease? Lease { get; private set; }

        public async ValueTask<AudioDeviceSnapshot> BuildAsync(AudioResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Lease = await (other ?? runtime).AcquireAsync(context, cancellationToken: cancellationToken);
            return Lease.State;
        }

        public ValueTask ReleaseAsync(AudioDeviceSnapshot resource, AudioResourceRuntime runtime) => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }
}
