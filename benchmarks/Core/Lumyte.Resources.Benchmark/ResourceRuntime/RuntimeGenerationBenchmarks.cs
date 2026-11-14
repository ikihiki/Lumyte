using BenchmarkDotNet.Attributes;
using Lumyte.Memory;

namespace Lumyte.Resources.Benchmark;

/// <summary>Rebuilds controlled runtime generations without physical device dependencies.</summary>
[BenchmarkCategory("Core", "Resources", "Generations", "Screening", "Comparison", "Diagnostic")]
public class RuntimeGenerationBenchmarks
{
    /// <summary>Invalidates one provider and rebuilds while an old lease remains bounded to one generation.</summary>
    /// <returns>The newly published provider version.</returns>
    [Benchmark]
    public async Task<ulong> InvalidateAndRebuildAsync()
    {
        await using var memory = new MemoryManager();
        var provider = new Provider();
        await using ResourceManager manager = new("generation", new ResourcesBenchmarks.UnusedSource(), [new Stage(provider)], memory, new ResourcesBenchmarks.InlineRuntime(), [provider]);
        using ResourceReference<ulong> reference = await manager.RetainAsync(new ResourceKey<ulong>("asset"));
        using ResourceLease<ulong> old = reference.Pin();
        provider.Advance();
        if (old.IsUsable)
        {
            throw new InvalidOperationException("Invalidated lease remained usable.");
        }

        ResourceReloadResult result = await manager.RebuildRuntimeAsync([provider.Id]);
        if (result.Status != ResourceReloadStatus.Published)
        {
            throw new InvalidOperationException("Runtime rebuild did not publish.");
        }

        using ResourceLease<ulong> current = reference.Pin();
        return current.Value;
    }

    private sealed class Provider : IResourceRuntimeDependency
    {
        public event EventHandler? Changed;

        public string Id => "controlled";

        public ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        internal void Advance()
        {
            Snapshot = new(2, true, 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Stage(Provider provider) : IResourceSourceStage<ulong>
    {
        public ValueTask<ulong> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            context.TrackRuntimeDependency(provider.Id, provider.Snapshot.Version);
            return ValueTask.FromResult(provider.Snapshot.Version);
        }

        public ValueTask ReleaseAsync(ulong resource) => ValueTask.CompletedTask;
    }
}
