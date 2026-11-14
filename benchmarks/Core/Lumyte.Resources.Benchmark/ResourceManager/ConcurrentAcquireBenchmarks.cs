using BenchmarkDotNet.Attributes;
using Lumyte.Memory;

namespace Lumyte.Resources.Benchmark;

/// <summary>Overlaps bounded callers against one gated construction.</summary>
[BenchmarkCategory("Core", "Resources", "Acquisition", "Concurrency", "Screening", "Comparison", "Diagnostic")]
public class ConcurrentAcquireBenchmarks
{
    /// <summary>Gets or sets the number of concurrent retain requests.</summary>
    [Params(1, 8, 64)]
    public int Callers { get; set; }

    /// <summary>Starts all callers before releasing construction, then returns all ownership.</summary>
    /// <returns>The number of actual shared builds.</returns>
    [Benchmark]
    public async Task<int> SharedBuildAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new GatedStage();
        await using ResourceManager manager = new("concurrent", new ResourcesBenchmarks.UnusedSource(), [stage], memory, new ResourcesBenchmarks.InlineRuntime());
        var pending = new Task<ResourceReference<int>>[Callers];
        try
        {
            for (int index = 0; index < pending.Length; index++)
            {
                pending[index] = manager.RetainAsync(new ResourceKey<int>("shared")).AsTask();
            }
        }
        finally
        {
            stage.Gate.TrySetResult();
        }

        ResourceReference<int>[] references = await Task.WhenAll(pending);
        foreach (ResourceReference<int> reference in references)
        {
            using (reference)
            using (ResourceLease<int> lease = reference.Pin())
            {
                if (lease.Value != 42)
                {
                    throw new InvalidOperationException("Shared output mismatch.");
                }
            }
        }

        return stage.Builds;
    }

    private sealed class GatedStage : IResourceSourceStage<int>
    {
        internal TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Builds { get; private set; }

        public async ValueTask<int> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            await Gate.Task;
            return 42;
        }

        public ValueTask ReleaseAsync(int resource) => ValueTask.CompletedTask;
    }
}
