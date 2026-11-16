using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.DependencyInjection;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Hosting;
using Lumyte.Mathematics.Colors;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics.Drawing;
using Lumyte.Resources.Graphics.Drawing2D;
using Lumyte.Resources.Graphics.Drawing2D.RenderGraph;
using Lumyte.Resources.Graphics.ImageEffects;
using Microsoft.Extensions.DependencyInjection;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Real DI resource pipelines with bounded cold/warm waves and managed device uploads.</summary>
public sealed class ResourceModelWorkload : IAsyncDisposable
{
    private static readonly ResourceKey<ModelMeshData> _cpuKey = new("triangle");

    private static readonly ResourceKey<GraphicsResource<ModelMesh>> _key = new("triangle");
    private readonly ServiceProvider _provider;
    private readonly ResourceReference<GraphicsResource<ModelMesh>>? _warm;
    private readonly ResourceReference<ModelMeshData>? _cpuWarm;

    /// <summary>Initializes a new instance of the <see cref="ResourceModelWorkload"/> class.</summary>
    /// <param name="warm">Whether to retain the primed mesh generation.</param>
    public ResourceModelWorkload(bool warm)
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.UseGraphics2D();
        services.AddSingleton<IResourceRuntime, CpuResourceRuntime>();
        services.AddGraphicsDevice<Factory>(ServiceLifetime.Singleton);
        services.AddGraphicsResource(_key, new ModelMeshDefinition(_cpuKey));
        services.AddResources(builder => builder.UseSource<MeshSource>()
            .AddSourcePipeline<ModelMeshData, MeshDecodeStage>()
            .AddGraphicsResourcePipeline<ModelMesh>()
            .AddGraphics2D().AddImageEffects());
        services.AddResourceGraphics();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Devices = _provider.GetRequiredService<IGraphicsDeviceService>();
        Devices.InitializeAsync().GetAwaiter().GetResult();
        Manager = _provider.GetRequiredService<ResourceManager>();
        Source = _provider.GetRequiredService<MeshSource>();
        Stage = _provider.GetServices<IResourceStage>().OfType<MeshDecodeStage>().Single();
        using GraphicsDeviceLease lease = Devices.AcquireAsync().GetAwaiter().GetResult();
        Device = (CpuGraphicsDevice)lease.Device;
        if (warm)
        {
            _cpuWarm = Manager.RetainAsync(_cpuKey).AsTask().GetAwaiter().GetResult();
            _warm = Manager.RetainAsync(_key).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Gets the real manager.</summary>
    public ResourceManager Manager { get; }

    /// <summary>Gets the real owner service.</summary>
    public IGraphicsDeviceService Devices { get; }

    /// <summary>Gets managed observations.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Gets observed source opens.</summary>
    public MeshSource Source { get; }

    /// <summary>Gets decode and release observations.</summary>
    public MeshDecodeStage Stage { get; }

    /// <summary>Retains, pins through a public publication view and graph imports, and releases a mesh.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    public async ValueTask<ulong> RetainPinImportAsync()
    {
        Device.ResetCounters();
        using ResourceReference<GraphicsResource<ModelMesh>> reference = await Manager.RetainAsync(_key);
        using ResourceSnapshot snapshot = Manager.CaptureSnapshot();
        using ResourceLease<GraphicsResource<ModelMesh>> pinned = reference.Pin(snapshot);
        using var graph = new Graph(Device);
        ModelMesh mesh = graph.ImportResources(Manager).UseSnapshot(snapshot).Acquire(reference).Value;
        mesh.Import(graph, new(PipelineStage.VertexShader, ResourceAccess.ShaderRead), new(PipelineStage.IndexInput, ResourceAccess.IndexRead));
        return mesh.IndexCount + (ulong)mesh.IndexFormat + pinned.Value.Value.Vertices.Count;
    }

    /// <summary>Runs a finite cold wave and trims every unowned CPU/GPU generation.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    public async ValueTask<ulong> ColdDecodeUploadTrimAsync()
    {
        ulong value = await RetainPinImportAsync();
        await Manager.TrimAsync();
        return value;
    }

    /// <summary>Rebuilds published graphics generations using the public runtime invalidation/rebuild boundary.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    public async ValueTask<ulong> RebuildGraphicsAsync()
    {
        GraphicsResourceRuntime runtime = _provider.GetRequiredService<GraphicsResourceRuntime>();
        await Manager.RebuildRuntimeAsync([runtime.Id]);
        ulong value = await RetainPinImportAsync();
        await Manager.TrimAsync();
        return value;
    }

    /// <summary>Acquires real renderer shader resources and creates/disposes an exact-generation renderer.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    public async ValueTask<ulong> RendererResourceAsync()
    {
        using ResourceReference<Renderer2DResource> reference = await Manager.AcquireRenderer2DAsync();
        using IServiceScope scope = _provider.CreateScope();
        Lumyte.Graphics.Drawing2D.DrawingCommandRenderers2D renderers = scope.ServiceProvider.GetRequiredService<Lumyte.Graphics.Drawing2D.DrawingCommandRenderers2D>();
        using Lumyte.Graphics.Drawing2D.Renderer2D renderer = reference.CreateRenderer(renderers);
        return (ulong)Device.Counters.ShaderCreates;
    }

    /// <summary>Uses the resource bridge and scoped DI strategies to append, compile and record a nonempty graph.</summary>
    /// <returns>The actual managed draw count.</returns>
    public async ValueTask<ulong> GraphRendererResourceAsync()
    {
        Device.ResetCounters();
        using ResourceReference<Renderer2DResource> reference = await Manager.AcquireRenderer2DAsync();
        using IServiceScope scope = _provider.CreateScope();
        DrawingCommandRenderers2D strategies = scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();
        using var graph = new Graph(Device);
        var description = new TextureDesc { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        Lumyte.Graphics.RenderGraph.RenderGraphTexture target = graph.CreateTexture(description);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(2, 3, 12, 10), new SRgba<float>(0.2f, 0.4f, 0.8f, 1));
        RenderGraphDrawList2D list = canvas.End();
        using RenderGraphRenderer2D renderer = reference.CreateRenderGraphRenderer(strategies, list, new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new DrawingGraphBindings2D(graph));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        graph.ExportTexture(target, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        return (ulong)Device.Counters.Draws;
    }

    /// <summary>Acquires and pins the real embedded image-effect shader package.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    public async ValueTask<ulong> EffectResourceAsync()
    {
        using ResourceReference<ImageEffectShaderResource> reference = await Manager.AcquireImageEffectShadersAsync();
        using ResourceLease<ImageEffectShaderResource> lease = reference.Pin();
        return (ulong)Device.Counters.ShaderCreates;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _warm?.Dispose();
        _cpuWarm?.Dispose();
        await Manager.DisposeAsync();
        await _provider.DisposeAsync();
    }

    /// <summary>Owns an actual managed recording device.</summary>
    public sealed class Factory : IGraphicsDeviceFactory
    {
        /// <inheritdoc/>
        public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IGraphicsDeviceOwner>(new CpuGraphicsDevice());
        }
    }
}
