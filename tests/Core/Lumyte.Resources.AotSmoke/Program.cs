using System.Diagnostics;
using System.Text;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Embedded;
using Lumyte.Resources.Execution;
using Lumyte.Resources.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.AotSmoke;

internal static class Program
{
    private static void Main(string[] args)
    {
        CheckSources();
        bool host = args.Contains("--host", StringComparer.Ordinal);
        var state = new CheckState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement<InjectedMemory>();
        services.AddSingleton<DeviceRuntime>();
        services.AddResources(builder => builder
            .UseSource<InjectedSource>()
            .AddSourcePipeline<Input, DecodeStage>()
            .AddSourcePipeline<StreamFactory, StreamStage>()
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
                Task<ResourceReference<Output>> acquisition = manager.RetainAsync(new ResourceKey<Output>("value")).AsTask();
                Wait(acquisition, execution);
                using (ResourceReference<Output> reference = acquisition.GetAwaiter().GetResult())
                using (ResourceLease<Output> lease = reference.Pin())
                {
                    Require(lease.Value.Number == 42 && lease.IsUsable, "Typed pipeline returned the wrong value or runtime generation.");
                }

                Task<int> trim = manager.TrimAsync().AsTask();
                Wait(trim, execution);
                Require(trim.GetAwaiter().GetResult() == 2, "Both output generations must be released.");
                CheckStreaming(manager, execution, state);
                Require(
                    provider.GetRequiredService<IMemoryManager>().GetSnapshot(new("cpu")).CommittedBytes == 0,
                    "Streaming buffers were not returned to the memory manager.");
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
            state.SourceConstructions == 1 && state.StageConstructions == 3 && state.DependencyConstructions == 1 && state.MemoryConstructions == 1,
            "Constructor-injected implementations were not resolved exactly once.");
        Require(
            state.InputReleases == 1 && state.OutputReleases == 1 && state.SnapshotReleases == 1 && state.MemoryReleases == 1,
            "The resource and DI lifetimes did not finish.");
        Require(
            state.StreamOpened == 1 && state.StreamSeeks == 1 && state.StreamDisposed == 1 && state.StreamReleases == 1,
            "The streaming reader and factory lifetimes did not finish.");
        Console.WriteLine($"Native AOT resources passed: host={host}, value=42, streaming=16, released=3.");
    }

    private static void CheckSources()
    {
        var embedded = new EmbeddedResourceSource(new() { Assembly = typeof(Program).Assembly });
        CheckSourceRange(embedded, new("source-probe"));
        var second = new EmbeddedResourceSource(new() { Assembly = typeof(Program).Assembly });
        Require(
            embedded.GetRevisionAsync(new("source-probe")).GetAwaiter().GetResult() == second.GetRevisionAsync(new("source-probe")).GetAwaiter().GetResult(),
            "An immutable assembly must have a stable source revision.");
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "input"), "0123456789");
            CheckSourceRange(new FileResourceSource(new() { RootPath = root }), new("input"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckSourceRange(IResourceSource source, ResourceId id)
    {
        ResourceSourceSnapshot snapshot = source.OpenSnapshotAsync(id).GetAwaiter().GetResult();
        using Stream reader = snapshot.OpenReadAsync(2, 3).GetAwaiter().GetResult();
        Require(snapshot.Revision == source.GetRevisionAsync(id).GetAwaiter().GetResult(), "Source revision changed unexpectedly.");
        snapshot.DisposeAsync().GetAwaiter().GetResult();
        Span<byte> bytes = stackalloc byte[3];
        reader.ReadExactly(bytes);
        Require(Encoding.UTF8.GetString(bytes) == "234" && reader.ReadByte() == -1, "Source range or reader ownership is invalid.");
    }

    private static void CheckStreaming(ResourceManager manager, ExecutionRuntime execution, CheckState state)
    {
        Task<ResourceStreamSession<int>> opening = manager.OpenStreamAsync<StreamFactory, int>(
            new ResourceKey<StreamFactory>("stream"), new ResourceStreamOptions { BlockSize = 4, MaxBufferedBlocks = 1 }).AsTask();
        Wait(opening, execution);
        ResourceStreamSession<int> session = opening.GetAwaiter().GetResult();
        try
        {
            int[] values = new int[4];
            Require(session.ReadAvailable(values) == 4 && values[0] == 0 && values[3] == 3, "Initial streaming block was invalid.");
            Task<int> pending = session.ReadAsync(values).AsTask();
            Require(!pending.IsCompleted, "A read must wait while the reader's input is unavailable.");
            state.StreamReadReady.SetResult();
            Wait(pending, execution);
            Require(pending.GetAwaiter().GetResult() == 4 && values[0] == 4 && values[3] == 7, "The streaming notification lost its data.");

            Wait(session.SeekAsync(10).AsTask(), execution);
            Require(session.Position == 10, "Streaming seek did not move to the requested element.");
            int expected = 10;
            while (true)
            {
                Task<int> read = session.ReadAsync(values).AsTask();
                Wait(read, execution);
                int count = read.GetAwaiter().GetResult();
                if (count == 0)
                {
                    break;
                }

                for (int index = 0; index < count; index++)
                {
                    Require(values[index] == expected++, "Streaming elements were reordered after seeking.");
                }
            }

            Require(expected == 16 && session.EndOfStream && session.Position == 16, "Streaming did not reach the expected end.");
        }
        finally
        {
            Wait(session.DisposeAsync().AsTask(), execution);
        }

        Task<int> trim = manager.TrimAsync().AsTask();
        Wait(trim, execution);
        Require(trim.GetAwaiter().GetResult() == 1, "The streaming factory generation was not released.");
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

    private readonly record struct Output(int Number, DeviceRuntime Runtime);

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

        internal int StreamOpened { get; set; }

        internal int StreamSeeks { get; set; }

        internal int StreamDisposed { get; set; }

        internal int StreamReleases { get; set; }

        internal TaskCompletionSource StreamReadReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private sealed class StreamStage : IResourceSourceStage<StreamFactory>
    {
        private readonly CheckState _state;

        public StreamStage(CheckState state)
        {
            _state = state;
            state.StageConstructions++;
        }

        public ValueTask<StreamFactory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new StreamFactory(_state));

        public ValueTask ReleaseAsync(StreamFactory resource)
        {
            _state.StreamReleases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StreamFactory(CheckState state) : IResourceStream<int>
    {
        public async ValueTask<IResourceStreamReader<int>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
        {
            Require(SynchronizationContext.Current is not null, "Reader creation must run in a job.");
            await Task.Yield();
            Require(SynchronizationContext.Current is not null, "Reader creation lost its job context.");
            state.StreamOpened++;
            return new StreamReader(state);
        }
    }

    private sealed class StreamReader(CheckState state) : IResourceStreamReader<int>
    {
        private int _position;

        public bool CanSeek => true;

        public async ValueTask<int> ReadAsync(Memory<int> destination, CancellationToken cancellationToken = default)
        {
            Require(SynchronizationContext.Current is not null, "Streaming decode must run in a job.");
            if (_position != 0)
            {
                await state.StreamReadReady.Task.WaitAsync(cancellationToken);
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Require(SynchronizationContext.Current is not null, "Streaming decode lost its job context.");
            int count = Math.Min(destination.Length, 16 - _position);
            for (int index = 0; index < count; index++)
            {
                destination.Span[index] = _position++;
            }

            return count;
        }

        public async ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
        {
            Require(SynchronizationContext.Current is not null, "Streaming seek must run in a job.");
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Require(SynchronizationContext.Current is not null, "Streaming seek lost its job context.");
            _position = checked((int)elementOffset);
            state.StreamSeeks++;
        }

        public async ValueTask DisposeAsync()
        {
            Require(SynchronizationContext.Current is not null, "Reader cleanup must run in a job.");
            await Task.Yield();
            Require(SynchronizationContext.Current is not null, "Reader cleanup lost its job context.");
            state.StreamDisposed++;
        }
    }
}
