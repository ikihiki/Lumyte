using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class Program
{
    private static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<Source>().AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>());
        ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, args.Contains("--host", StringComparer.Ordinal) ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, 1, new());
        Wait(execution.StartAsync().AsTask(), execution);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        var graph = new Lumyte.Graphics.RenderGraph.RenderGraph();
        using ResourceRenderGraphImports imports = graph.ImportResources(manager);
        Task<ShaderArtifact> loading = imports.AcquireAsync(new ResourceKey<ShaderArtifact>("shader")).AsTask();
        Wait(loading, execution);
        Require(loading.Result.GetTarget(ShaderTarget.Wgsl).Code.Length == 1);
        using CompiledRenderGraph plan = imports.Compile();
        imports.Dispose();
        Task<int> trimming = manager.TrimAsync().AsTask();
        Wait(trimming, execution);
        Require(trimming.Result == 0);
        plan.Dispose();
        trimming = manager.TrimAsync().AsTask();
        Wait(trimming, execution);
        Require(trimming.Result == 1);
        Wait(manager.DisposeAsync().AsTask(), execution);
        Wait(execution.StopAsync().AsTask(), execution);
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("Native AOT shader loading and graph retention passed.");
    }

    private static void Wait(Task task, ExecutionRuntime execution)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!task.IsCompleted)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            execution.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            execution.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new InvalidOperationException("Shader loading or imported resource ownership failed.");
        }
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => new(new Snapshot(id));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new("1");
    }

    private sealed class Snapshot(ResourceId id) : ResourceSourceSnapshot
    {
        private readonly byte[] _binary = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], "{}").GetBinary();

        public override ResourceId Id => id;

        public override string Revision => "1";

        public override long? Length => _binary.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default) => new(new MemoryStream(_binary, writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
