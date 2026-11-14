using BenchmarkDotNet.Attributes;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Embedded;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Benchmark;

/// <summary>Uses actual embedded readers, DI registrations and Execution workflows.</summary>
public class IntegrationBenchmarks
{
    private MemoryManager _memory = null!;
    private ResourceRuntime _resources = null!;
    private ExecutionRuntime _execution = null!;
    private EmbeddedResourceSource _source = null!;

    /// <summary>Starts the real two-worker execution runtime outside timed operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _memory = new MemoryManager();
        _resources = new ResourceRuntime();
        _execution = new ExecutionRuntime([_resources]);
        _execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        _execution.StartAsync().GetAwaiter().GetResult();
        _source = new EmbeddedResourceSource(new EmbeddedResourceSourceOptions { Assembly = typeof(IntegrationBenchmarks).Assembly });
    }

    /// <summary>Stops execution only after all owned managers have completed.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _execution.StopAsync().AsTask().GetAwaiter().GetResult();
        _memory.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Registers and initially resolves a real singleton resource manager.</summary>
    /// <returns>Whether a child DI scope resolves the same manager.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-12")]
    public async Task<bool> RegisterAndResolveAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryManager>(_memory);
        services.AddSingleton<IResourceRuntime>(new ResourcesBenchmarks.InlineRuntime());
        services.AddSingleton<IResourceSource>(_source);
        services.AddResources(builder => builder.UseSource<EmbeddedResourceSource>().AddSourcePipeline<int, ChecksumStage>());
        services.AddSingleton(_source);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using IServiceScope scope = provider.CreateScope();
        return ReferenceEquals(manager, scope.ServiceProvider.GetRequiredService<ResourceManager>());
    }

    /// <summary>Opens an independent bounded range in an already loaded assembly.</summary>
    /// <returns>The range byte sum.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-13")]
    public async Task<int> EmbeddedRangeAsync()
    {
        await using ResourceSourceSnapshot snapshot = await _source.OpenSnapshotAsync(new ResourceId("Benchmark.Payload"));
        await using Stream stream = await snapshot.OpenReadAsync(2, 4);
        int sum = 0;
        int value;
        while ((value = stream.ReadByte()) >= 0)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>Builds a checksum through the real embedded source pipeline.</summary>
    /// <returns>The complete payload checksum.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-14")]
    public async Task<int> EmbeddedPipelineAsync()
    {
        await using ResourceManager manager = new("embedded", _source, [new ChecksumStage()], _memory, new ResourcesBenchmarks.InlineRuntime());
        using ResourceReference<int> reference = await manager.RetainAsync(new ResourceKey<int>("Benchmark.Payload"));
        using ResourceLease<int> lease = reference.Pin();
        return lease.Value;
    }

    /// <summary>Builds and releases a four-node chain through actual Execution jobs.</summary>
    /// <returns>The chain result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-15")]
    public async Task<int> ExecutionChainAsync()
    {
        await using ResourceManager manager = ResourcesBenchmarks.CreateManager(_memory, 4, _resources);
        using ResourceReference<int> reference = await manager.RetainAsync(new ResourceKey<int>("asset/root"));
        using ResourceLease<int> lease = reference.Pin();
        return lease.Value;
    }

    /// <summary>Rejects construction before the actual Execution runtime starts and drains failed ownership.</summary>
    /// <returns>Whether the admission boundary rejected the request.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-16")]
    public async Task<bool> BeforeStartRejectedAsync()
    {
        var resources = new ResourceRuntime();
        await using ResourceManager manager = ResourcesBenchmarks.CreateManager(_memory, 1, resources);
        try
        {
            using ResourceReference<int> reference = await manager.RetainAsync(new ResourceKey<int>("asset"));
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    internal sealed class ChecksumStage : IResourceSourceStage<int>
    {
        public async ValueTask<int> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
            int sum = 0;
            int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                sum += value;
            }

            return sum;
        }

        public ValueTask ReleaseAsync(int resource) => ValueTask.CompletedTask;
    }
}
