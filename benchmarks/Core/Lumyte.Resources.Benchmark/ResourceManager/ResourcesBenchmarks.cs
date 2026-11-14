using BenchmarkDotNet.Attributes;
using Lumyte.Memory;

namespace Lumyte.Resources.Benchmark;

/// <summary>Exercises real manager construction, caching and generation ownership.</summary>
[BenchmarkCategory("Lifetime", "Standalone", "Core", "Resources", "Acquisition", "Screening", "Comparison", "Diagnostic")]
public class ResourcesBenchmarks
{
    private readonly ResourceKey<int> _key = new("asset/root");
    private MemoryManager _memory = null!;
    private ResourceManager _manager = null!;
    private ResourceReference<int> _resident = null!;

    /// <summary>Gets or sets the finite dependency chain depth.</summary>
    [Params(1, 8, 32)]
    public int Depth { get; set; }

    /// <summary>Initializes one resident generation outside measured operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _memory = new MemoryManager();
        _manager = CreateManager(_memory, Depth);
        _resident = _manager.RetainAsync(_key).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Returns all external ownership before disposing manager and memory.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _resident.Dispose();
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _memory.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Validates an ordinal logical key.</summary>
    /// <returns>The constructed identifier.</returns>
    [Benchmark]
    public ResourceId ConstructKey() => new("asset/material/diffuse");

    /// <summary>Acquires and returns a reference to an already published generation.</summary>
    /// <returns>The retained value.</returns>
    [Benchmark]
    public int CacheHit()
    {
        using ResourceReference<int> reference = _manager.RetainAsync(_key).AsTask().GetAwaiter().GetResult();
        using ResourceLease<int> lease = reference.Pin();
        return lease.Value;
    }

    /// <summary>Builds a fresh dependency chain and releases it, with no growing key set.</summary>
    /// <returns>The root value.</returns>
    [Benchmark]
    public async Task<int> BuildChainAsync()
    {
        await using ResourceManager manager = CreateManager(_memory, Depth);
        using ResourceReference<int> reference = await manager.RetainAsync(_key);
        using ResourceLease<int> lease = reference.Pin();
        return lease.Value;
    }

    /// <summary>Retains an existing handle and returns both reference and lease.</summary>
    /// <returns>The pinned scalar.</returns>
    [Benchmark]
    public int RetainHandle()
    {
        if (!_manager.TryRetain(_resident.Handle, out ResourceReference<int>? reference))
        {
            throw new InvalidOperationException("Resident handle unexpectedly expired.");
        }

        using (reference)
        using (ResourceLease<int> lease = reference.Pin())
        {
            return lease.Value;
        }
    }

    /// <summary>Captures a consistent generation map and pins its resident handle.</summary>
    /// <returns>The captured scalar.</returns>
    [Benchmark]
    public int Snapshot()
    {
        using ResourceSnapshot snapshot = _manager.CaptureSnapshot();
        if (!snapshot.TryPin(_resident.Handle, out ResourceLease<int>? lease))
        {
            throw new InvalidOperationException("Snapshot omitted resident generation.");
        }

        using (lease)
        {
            return lease.Value;
        }
    }

    /// <summary>Builds a finite graph, releases external ownership, then trims real outputs.</summary>
    /// <returns>The number of released graph outputs.</returns>
    [Benchmark]
    public async Task<int> BuildAndTrimAsync()
    {
        await using ResourceManager manager = CreateManager(_memory, Depth);
        ResourceReference<int> reference = await manager.RetainAsync(_key);
        reference.Dispose();
        return await manager.TrimAsync();
    }

    internal static ResourceManager CreateManager(IMemoryManager memory, int depth, IResourceRuntime? runtime = null)
        => new("benchmark", new UnusedSource(), [new ChainStage(depth)], memory, runtime ?? new InlineRuntime());

    internal sealed class ChainStage(int depth) : IResourceSourceStage<int>
    {
        public async ValueTask<int> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            int index = context.Address.Subresource.Length == 0 ? 0 : int.Parse(context.Address.Subresource, System.Globalization.CultureInfo.InvariantCulture);
            if (index + 1 >= depth)
            {
                return 1;
            }

            int child = await context.RequireAsync(new ResourceKey<int>(new ResourceAddress(context.Address.Id, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))), ResourceDependencyLifetime.Resident, cancellationToken);
            return child + 1;
        }

        public ValueTask ReleaseAsync(int resource) => ValueTask.CompletedTask;
    }

    internal sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }

    internal sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
