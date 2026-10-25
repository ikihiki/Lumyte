using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class CreationScenario
{
    internal static void Run(AudioModule audio, ResourceRuntime executor, AudioResourceRuntime runtime, TestFactory factory, Action<Task> wait)
    {
        TestDevice device = factory.Current;
        var format = new AudioFormat(48000, 1);
        ReadOnlyMemory<float> samples = new float[48];
        device.FailNextClipCreation();
        ExpectCreationFailure(() => wait(audio.State.CreateClipWithLease(format, samples).AsTask()));
        Task<(AudioDeviceLease Lease, IAudioClip Clip, Exception? ValidationError)> create = audio.State.CreateClipWithLease(format, samples).AsTask();
        wait(create);
        (AudioDeviceLease lease, IAudioClip clip, Exception? validationError) = create.GetAwaiter().GetResult();
        try
        {
            if (validationError is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(validationError);
            }

            Require(lease.IsUsable, "A new clip unexpectedly belongs to an unavailable device.");
            device.FailNextVoiceCreation();
            ExpectCreationFailure(() => wait(new AudioClip(clip).CreateVoice(AudioBus.Effects).AsTask()));
        }
        finally
        {
            wait(new AudioClip(clip).DestroyClip().AsTask());
            lease.Dispose();
        }

        var memory = new MemoryManager();
        var manager = new ResourceManager("untracked-audio", new UnusedSource(), [new UntrackedClipStage(runtime)], memory, executor);
        try
        {
            int created = device.CreatedClips;
            int returned = device.ReturnedClips;
            device.FailNextClipRelease();
            try
            {
                Task<ResourceReference<AudioClip>> load = manager.AcquireAsync(new ResourceKey<AudioClip>("untracked")).AsTask();
                wait(load);
                using ResourceReference<AudioClip> unexpected = load.GetAwaiter().GetResult();
                throw new InvalidOperationException("A clip without its registered runtime dependency was published.");
            }
            catch (AggregateException error)
            {
                IReadOnlyCollection<Exception> errors = error.Flatten().InnerExceptions;
                if (!errors.Any(exception => exception is ArgumentException)
                    || !errors.Any(exception => exception.Message == "Injected clip release failure."))
                {
                    throw;
                }
            }

            Require(device.CreatedClips == created + 1 && device.ReturnedClips == returned, "Failed native cleanup lost the unpublished clip.");
            device.SetOutput(AudioOutputState.Unavailable);
            wait(audio.State.Refresh().AsTask());
            wait(audio.Devices.RecoverAsync().AsTask());
            Task<int> retained = audio.Devices.RetireAsync().AsTask();
            wait(retained);
            Require(retained.GetAwaiter().GetResult() == 0 && !device.IsDisposed, "An unpublished native child lost its device lease before cleanup retry.");

            wait(manager.TrimAsync().AsTask());
            Task<int> retired = audio.Devices.RetireAsync().AsTask();
            wait(retired);
            Require(device.ReturnedClips == returned + 1 && retired.GetAwaiter().GetResult() == 1 && device.IsDisposed, "Creation failure leaked a lease or cleanup retry did not release the original child exactly once.");
            Console.WriteLine("Resource audio creation passed: one owner request, failed creation returns leases, failed tracking retains native cleanup and its generation until retry.");
        }
        finally
        {
            wait(manager.DisposeAsync().AsTask());
            wait(memory.DisposeAsync().AsTask());
        }

        ValidationFailure(audio, executor, runtime, factory, wait);
    }

    private static void ValidationFailure(AudioModule audio, ResourceRuntime executor, AudioResourceRuntime runtime, TestFactory factory, Action<Task> wait)
    {
        TestDevice device = factory.Current;
        var memory = new MemoryManager();
        var manager = new ResourceManager("failed-audio-validation", new UnusedSource(), [new UntrackedClipStage(runtime)], memory, executor);
        try
        {
            int returned = device.ReturnedClips;
            device.FailOutputAfterNextClip();
            device.FailNextClipRelease();
            try
            {
                Task<ResourceReference<AudioClip>> load = manager.AcquireAsync(new ResourceKey<AudioClip>("validation-failure")).AsTask();
                wait(load);
                using ResourceReference<AudioClip> unexpected = load.GetAwaiter().GetResult();
                throw new InvalidOperationException("A clip whose device validation threw was published.");
            }
            catch (AggregateException error)
            {
                IReadOnlyCollection<Exception> errors = error.Flatten().InnerExceptions;
                Require(errors.Any(exception => exception.Message == "Injected post-creation output query failure.")
                    && errors.Any(exception => exception.Message == "Injected clip release failure."),
                    "The original validation error or failed native cleanup was lost.");
            }

            Require(audio.Devices.State.Status == AudioDeviceServiceStatus.Lost && device.ReturnedClips == returned,
                "Post-creation validation did not invalidate and retain the candidate.");
            wait(audio.Devices.RecoverAsync().AsTask());
            Task<int> retained = audio.Devices.RetireAsync().AsTask();
            wait(retained);
            Require(retained.GetAwaiter().GetResult() == 0 && !device.IsDisposed,
                "The failed validation candidate lost its exact device lease before cleanup retry.");
            wait(manager.TrimAsync().AsTask());
            Task<int> retired = audio.Devices.RetireAsync().AsTask();
            wait(retired);
            Require(retired.GetAwaiter().GetResult() == 1 && device.IsDisposed && device.ReturnedClips == returned + 1,
                "Retry did not release the failed validation candidate and its original device exactly once.");
            Console.WriteLine("Resource audio validation passed: post-create query failure retains native child and exact generation through cleanup retry.");
        }
        finally
        {
            wait(manager.DisposeAsync().AsTask());
            wait(memory.DisposeAsync().AsTask());
        }
    }

    private static void ExpectCreationFailure(Action create)
    {
        try
        {
            create();
        }
        catch (InvalidOperationException error) when (error.Message is "Injected clip creation failure." or "Injected voice creation failure.")
        {
            return;
        }

        throw new InvalidOperationException("Injected native creation failure was not observed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }

    private sealed class UntrackedClipStage(AudioResourceRuntime runtime) : IResourceSourceStage<AudioClip>
    {
        public ValueTask<AudioClip> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => runtime.CreateClipAsync(new(new(48000, 1), new float[48]), context, cancellationToken);

        public ValueTask ReleaseAsync(AudioClip clip) => runtime.DestroyClipAsync(clip);
    }
}
