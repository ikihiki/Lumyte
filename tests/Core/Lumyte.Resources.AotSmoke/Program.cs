using System.Diagnostics;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.AotSmoke;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        var state = new CheckState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement<InjectedMemory>();
        services.AddSingleton<DeviceRuntime>();
        services.AddResources(builder => builder
            .UseSource<InjectedSource>()
            .AddSourcePipeline<Input, DecodeStage>()
            .AddTransformPipeline<Input, Output, DeviceStage>()
            .UseStageRuntime<Output, DeviceRuntime>()
            .UseRuntimeDependency<InjectedDependency>("device"));
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        Wait(execution.StartAsync().AsTask(), execution);
        try
        {
            AsyncServiceScope scope = provider.CreateAsyncScope();
            try
            {
                ResourceManager manager = scope.ServiceProvider.GetRequiredService<ResourceManager>();
                Require(ReferenceEquals(manager, provider.GetRequiredService<ResourceManager>()), "Scopes must share one resource manager.");
                Task<ResourceReference<Output>> acquisition = manager.AcquireAsync(new ResourceKey<Output>("value")).AsTask();
                Wait(acquisition, execution);
                using (ResourceReference<Output> reference = acquisition.GetAwaiter().GetResult())
                using (ResourceLease<Output> lease = reference.Acquire())
                {
                    Require(lease.Value.Number == 42 && lease.IsUsable, "Typed pipeline returned the wrong value or runtime generation.");
                }

                Task<int> trim = manager.TrimAsync().AsTask();
                Wait(trim, execution);
                Require(trim.GetAwaiter().GetResult() == 2, "Both output generations must be released.");
                Wait(manager.DisposeAsync().AsTask(), execution);
            }
            finally
            {
                Wait(scope.DisposeAsync().AsTask(), execution);
            }
        }
        finally
        {
            Wait(execution.StopAsync().AsTask(), execution);
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Require(
            state.SourceConstructions == 1 && state.StageConstructions == 2 && state.DependencyConstructions == 1 && state.MemoryConstructions == 1,
            "Constructor-injected implementations were not resolved exactly once.");
        Require(
            state.InputReleases == 1 && state.OutputReleases == 1 && state.SnapshotReleases == 1 && state.MemoryReleases == 1,
            "The resource and DI lifetimes did not finish.");
        Console.WriteLine($"Native AOT resources passed: host={host}, value=42, released=2.");
    }

    private static void Wait(Task task, ExecutionRuntime execution)
    {
        long started = Stopwatch.GetTimestamp();
        while (!task.IsCompleted)
        {
            execution.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            execution.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The resource workflow did not complete.");
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CheckState
    {
        internal int SourceConstructions { get; set; }

        internal int StageConstructions { get; set; }

        internal int DependencyConstructions { get; set; }

        internal int MemoryConstructions { get; set; }

        internal int InputReleases { get; set; }

        internal int OutputReleases { get; set; }

        internal int SnapshotReleases { get; set; }

        internal int MemoryReleases { get; set; }
    }

    // These constructors are reached only through DI. Manual construction would hide trimming regressions.
    private sealed class InjectedMemory : IMemoryManager
    {
        private readonly MemoryManager _inner = new();
        private readonly CheckState _state;

        public InjectedMemory(CheckState state)
        {
            _state = state;
            state.MemoryConstructions++;
        }

        public IMemoryScope CreateScope(string owner) => _inner.CreateScope(owner);

        public MemorySnapshot GetSnapshot(MemoryPoolId pool) => _inner.GetSnapshot(pool);

        public IDisposable RegisterReclaimer(IMemoryReclaimer reclaimer) => _inner.RegisterReclaimer(reclaimer);

        public ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
            => _inner.ReclaimAsync(pool, targetBytes, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _state.MemoryReleases++;
        }
    }

    private sealed class InjectedSource : IResourceSource
    {
        private readonly CheckState _state;

        public InjectedSource(CheckState state)
        {
            _state = state;
            state.SourceConstructions++;
        }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, _state));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("one");
    }

    private sealed class Snapshot(ResourceId id, CheckState state) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "one";

        public override long? Length => 1;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream([21], false));

        public override ValueTask DisposeAsync()
        {
            state.SnapshotReleases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InjectedDependency : IResourceRuntimeDependency
    {
        public InjectedDependency(CheckState state)
        {
            state.DependencyConstructions++;
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public string Id => "device";

        public ResourceRuntimeSnapshot Snapshot => new(1, true, 0);
    }

    private sealed class DeviceRuntime(ResourceRuntime runtime) : IResourceRuntime
    {
        public IResourceExecutor Executor => runtime.Executor;

        internal int Multiplier => 2;
    }

    private sealed record Input(int Number);

    private readonly record struct Output(int Number, DeviceRuntime Runtime);

    private sealed class DecodeStage : IResourceSourceStage<Input>
    {
        private readonly CheckState _state;

        public DecodeStage(CheckState state)
        {
            _state = state;
            state.StageConstructions++;
        }

        public async ValueTask<Input> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Require(SynchronizationContext.Current is not null, "The default runtime must dispatch a workflow.");
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
            int number = stream.ReadByte();
            await Task.Yield();
            Require(SynchronizationContext.Current is not null, "The workflow continuation lost its execution context.");
            return new(number);
        }

        public ValueTask ReleaseAsync(Input resource)
        {
            _state.InputReleases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeviceStage : IResourceTransform<Input, Output, DeviceRuntime>
    {
        private readonly CheckState _state;

        public DeviceStage(CheckState state)
        {
            _state = state;
            state.StageConstructions++;
        }

        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public ValueTask<Output> BuildAsync(Input input, DeviceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            context.TrackRuntimeDependency("device", 1);
            return ValueTask.FromResult(new Output(input.Number * runtime.Multiplier, runtime));
        }

        public ValueTask ReleaseAsync(Output resource, DeviceRuntime runtime)
        {
            Require(ReferenceEquals(resource.Runtime, runtime), "Release must use the original typed environment.");
            _state.OutputReleases++;
            return ValueTask.CompletedTask;
        }
    }
}
