using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics.ImageEffects;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        VerifyShaderLoading(host);
        VerifyImageEffects(host, false);
        VerifyImageEffects(host, true);
        Console.WriteLine("Native AOT shader loading, default/custom image effects and graph retention passed.");
    }

    private static void VerifyShaderLoading(bool host)
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<Source>().AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>());
        ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, 1, new());
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
    }

    private static void VerifyImageEffects(bool host, bool custom)
    {
        var device = new SmokeDevice();
        var source = new Source();
        var services = new ServiceCollection();
        services.AddSingleton(device);
        services.AddSingleton(source);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<SmokeFactory>(ServiceLifetime.Singleton);
        if (custom)
        {
            services.AddSingleton(new ImageEffectShaderPackage(new("custom/fullscreen"), new("custom/effects")));
        }

        services.AddResources(builder => builder.UseSource<Source>()
            .AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()
            .AddImageEffects());
        services.AddResourceGraphics();
        ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, 1, new());
        Wait(execution.StartAsync().AsTask(), execution);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        Wait(devices.InitializeAsync().AsTask(), execution);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        Task<ResourceReference<ImageEffectShaderResource>> loading = manager.AcquireImageEffectShadersAsync().AsTask();
        Wait(loading, execution);
        using ResourceReference<ImageEffectShaderResource> reference = loading.Result;
        using (ResourceLease<ImageEffectShaderResource> lease = reference.Acquire())
        {
            Require(ReferenceEquals(device, lease.Value.Device) && lease.Value.Epoch > 0);
            Require(lease.Value.Shaders.Fullscreen.GetTarget(ShaderTarget.Wgsl).Stage == ShaderStage.Vertex);
            Require(lease.Value.Shaders.Effects.GetTarget(ShaderTarget.Wgsl).Stage == ShaderStage.Fragment);
            Require(custom != ReferenceEquals(lease.Value.Shaders.Fullscreen, ImageEffectShaders.Embedded.Fullscreen));
        }

        Require(source.Opens == (custom ? 2 : 0));
        Require(device.Shaders.Count == 2);
        var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphTexture texture = graph.CreateTexture(new TextureDesc
        {
            Width = 1,
            Height = 1,
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.Sampled,
        });
        var image = new EffectImage(texture, new(0, 0, 1, 1), EffectColorSemantic.DisplayLinear);
        var owned = new OwnedResource();
        using ResourceRenderGraphImports imports = graph.ImportResources(manager);
        Require(imports.AppendEffect(reference, new OwningEffect(owned), image) == image);
        using CompiledRenderGraph plan = imports.Compile();
        imports.Dispose();
        reference.Dispose();
        Task<int> trimming = manager.TrimAsync().AsTask();
        Wait(trimming, execution);
        Require(trimming.Result == 0 && !owned.Disposed && device.Shaders.TrueForAll(shader => !shader.Disposed));
        plan.Dispose();
        Require(owned.Disposed);
        Wait(manager.TrimAsync().AsTask(), execution);
        Require(device.Shaders.TrueForAll(shader => shader.Disposed));
        Wait(manager.DisposeAsync().AsTask(), execution);
        Wait(execution.StopAsync().AsTask(), execution);
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        internal int Opens { get; private set; }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            Opens++;
            return new(new Snapshot(id));
        }

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new("1");
    }

    private sealed class Snapshot(ResourceId id) : ResourceSourceSnapshot
    {
        private readonly byte[] _binary = CreateBinary(id);

        public override ResourceId Id => id;

        public override string Revision => "1";

        public override long? Length => _binary.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default) => new(new MemoryStream(_binary, writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static byte[] CreateBinary(ResourceId id)
        {
            ShaderStage stage = id.Value switch
            {
                "custom/fullscreen" => ShaderStage.Vertex,
                "custom/effects" => ShaderStage.Fragment,
                _ => ShaderStage.Compute,
            };
            return ShaderArtifact.PackTarget(ShaderTarget.Wgsl, stage, "main", "test", [1], "{}").GetBinary();
        }
    }

    private sealed class OwnedResource : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed record OwningEffect(OwnedResource Resource) : ImageEffectDefinition
    {
        public override int InputCount => 1;

        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        public override EffectImage AppendPasses(EffectPreparationContext context, Lumyte.Graphics.RenderGraph.RenderGraph graph, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(Resource);
            return inputs[0];
        }
    }
}
