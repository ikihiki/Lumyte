using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Checks keyed definitions, native compilation options, dependency ownership and arbitrary composite outputs.</summary>
public sealed class GraphicsDefinitionTests
{
    private static readonly ResourceKey<GraphicsResource<IGraphicsShader>> _vertex = new("shared-vertex");
    private static readonly ResourceKey<GraphicsResource<IGraphicsShader>> _fragment = new("shared-fragment");
    private static readonly ResourceKey<GraphicsResource<IGraphicsPipeline>> _first = new("first");
    private static readonly ResourceKey<GraphicsResource<IGraphicsPipeline>> _second = new("second");
    private static readonly ResourceKey<GraphicsResource<IGraphicsSampler>> _sampler = new("sampler");
    private static readonly ResourceKey<GraphicsResource<TrackedAsset>> _asset = new("custom-asset");

    /// <summary>Checks multiple keys, last registration wins, shader sharing and all compile settings.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task KeyedPipelinesShareShadersAndPreserveCompilationSettingsAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, collection =>
        {
            RegisterShaders(collection);
            collection.AddGraphicsResource(_first, new GraphicsPipelineDefinition { VertexShader = _vertex, FragmentShader = _fragment, Label = "replaced" });
            collection.AddGraphicsResource(_first, new GraphicsPipelineDefinition
            {
                VertexShader = _vertex,
                FragmentShader = _fragment,
                Label = "selected",
                TopologyClass = PrimitiveTopologyClass.Line,
                AlphaToCoverageEnable = true,
                Optimization = PipelineOptimizationMode.FullSpecialization,
            });
            collection.AddGraphicsResource(_second, new GraphicsPipelineDefinition { VertexShader = _vertex });
        });
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        TestDevice device = Assert.Single(state.Devices);
        Assert.Empty(device.Shaders);
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsResource<IGraphicsPipeline>> first = await manager.RetainAsync(_first);
        using ResourceReference<GraphicsResource<IGraphicsPipeline>> second = await manager.RetainAsync(_second);
        using ResourceLease<GraphicsResource<IGraphicsPipeline>> lease = first.Pin();
        GraphicsPipelineDesc desc = lease.Value.Value.Desc;
        Assert.Equal("selected", desc.Label);
        Assert.Equal(PrimitiveTopologyClass.Line, desc.TopologyClass);
        Assert.True(desc.AlphaToCoverageEnable);
        Assert.Equal(PipelineOptimizationMode.FullSpecialization, desc.Optimization);
        using ResourceLease<GraphicsResource<IGraphicsPipeline>> other = second.Pin();
        Assert.Same(desc.VertexShader, other.Value.Value.Desc.VertexShader);
        Assert.Null(other.Value.Value.Desc.FragmentShader);
        Assert.Equal(2, device.Shaders.Count);
        Assert.Equal(2, device.Programs.Count);
        first.Dispose();
        lease.Dispose();
        await manager.TrimAsync();
        Assert.False(Assert.IsType<TestShader>(desc.VertexShader).Disposed);
        second.Dispose();
        other.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RetainAsync(new ResourceKey<GraphicsResource<IGraphicsPipeline>>("missing")).AsTask());
    }

    /// <summary>Checks reusable definitions support arbitrary composite assets and retain dependencies through cleanup retry.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CustomAssetRetainsDependenciesUntilCleanupSucceedsAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, collection =>
        {
            collection.AddGraphicsResource(_sampler, new GraphicsSamplerDefinition(new() { MaxAnisotropy = 4 }));
            collection.AddGraphicsResource(_asset, new AssetDefinition(_sampler));
        });
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsResource<TrackedAsset>> reference = await manager.RetainAsync(_asset);
        using ResourceLease<GraphicsResource<TrackedAsset>> lease = reference.Pin();
        TrackedAsset asset = lease.Value.Value;
        Assert.Equal(4u, asset.Sampler.Desc.MaxAnisotropy);
        asset.FailOnce = true;
        reference.Dispose();
        lease.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => manager.TrimAsync().AsTask());
        Assert.False(asset.Disposed);
        Assert.False(asset.Sampler.Disposed);
        await manager.TrimAsync();
        Assert.True(asset.Disposed);
        Assert.True(asset.Sampler.Disposed);
    }

    /// <summary>Checks graphics resource definitions rebuild against a new device while retaining shader CPU inputs.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RebuildUsesTheSameLogicalKeyAndCpuArtifactsAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, collection =>
        {
            RegisterShaders(collection);
            collection.AddGraphicsResource(_first, new GraphicsPipelineDefinition { VertexShader = _vertex, FragmentShader = _fragment });
        });
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        IGraphicsDeviceService devices = services.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsResource<IGraphicsPipeline>> reference = await manager.RetainAsync(_first);
        using ResourceLease<GraphicsResource<IGraphicsPipeline>> old = reference.Pin();
        ShaderArtifact vertex = old.Value.Value.Desc.VertexShader.Artifact;
        state.Devices[0].Lose();
        Assert.Throws<ResourceUnavailableException>(() => old.Value);
        await devices.RecoverAsync();
        Assert.Equal(ResourceReloadStatus.Published, (await manager.RebuildRuntimeAsync([services.GetRequiredService<GraphicsResourceRuntime>().Id])).Status);
        using ResourceLease<GraphicsResource<IGraphicsPipeline>> current = reference.Pin();
        Assert.Same(state.Devices[1], current.Value.Device);
        Assert.Same(vertex, current.Value.Value.Desc.VertexShader.Artifact);
        old.Dispose();
        await manager.TrimAsync();
        Assert.All(state.Devices[0].Shaders, shader => Assert.True(shader.Disposed));
    }

    /// <summary>Checks compute dependencies, shared views and allocations use the same keyed definition mechanism.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ComputeAndTextureViewsRetainTheirSharedDependenciesAsync()
    {
        var state = new GraphicsTestState();
        var shaderKey = new ResourceKey<GraphicsResource<IGraphicsShader>>("compute-shader");
        var computeKey = new ResourceKey<GraphicsResource<IGraphicsComputePipeline>>("compute");
        var textureKey = new ResourceKey<GraphicsResource<IGraphicsTexture>>("texture");
        var viewKey = new ResourceKey<GraphicsResource<IGraphicsTextureView>>("view");
        await using ServiceProvider services = CreateServices(state, collection =>
        {
            collection.AddGraphicsResource(shaderKey, new GraphicsShaderDefinition(static () => Artifact(ShaderStage.Compute)));
            collection.AddGraphicsResource(computeKey, new GraphicsComputePipelineDefinition(shaderKey, "compute-label"));
            collection.AddGraphicsResource(textureKey, new GraphicsTextureDefinition(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled }));
            collection.AddGraphicsResource(viewKey, new GraphicsTextureViewDefinition(textureKey));
        });
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsResource<IGraphicsComputePipeline>> compute = await manager.RetainAsync(computeKey);
        using ResourceLease<GraphicsResource<IGraphicsComputePipeline>> computeLease = compute.Pin();
        TestComputePipeline pipeline = Assert.IsType<TestComputePipeline>(computeLease.Value.Value);
        Assert.Equal("compute-label", pipeline.Desc.Label);
        using ResourceReference<GraphicsResource<IGraphicsTextureView>> view = await manager.RetainAsync(viewKey);
        using ResourceLease<GraphicsResource<IGraphicsTextureView>> viewLease = view.Pin();
        TestTexture texture = Assert.IsType<TestTexture>(viewLease.Value.Value.Texture);
        Assert.Equal(8u, texture.Width);
        Assert.Equal(4u, texture.Height);
        view.Dispose();
        await manager.TrimAsync();
        Assert.False(texture.Disposed);
        viewLease.Dispose();
        await manager.TrimAsync();
        Assert.True(texture.Disposed);
        compute.Dispose();
        computeLease.Dispose();
        await manager.TrimAsync();
        Assert.True(pipeline.Disposed);
        Assert.All(state.Devices[0].Shaders, shader => Assert.True(shader.Disposed));
    }

    private static ServiceProvider CreateServices(GraphicsTestState state, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        configure(services);
        services.AddResources(builder => builder.UseSource<TestSource>().AddGraphicsResources().AddGraphicsResources()
            .AddGraphicsResourcePipeline<TrackedAsset>());
        services.AddResourceGraphics();
        return services.BuildServiceProvider();
    }

    private static void RegisterShaders(IServiceCollection services)
    {
        services.AddGraphicsResource(_vertex, new GraphicsShaderDefinition(static () => Artifact(ShaderStage.Vertex)));
        services.AddGraphicsResource(_fragment, new GraphicsShaderDefinition(static () => Artifact(ShaderStage.Fragment)));
    }

    private static ShaderArtifact Artifact(ShaderStage stage) => ShaderArtifact.PackTarget(ShaderTarget.Wgsl, stage, "main", "test", [1], "{}");

    private sealed class AssetDefinition(ResourceKey<GraphicsResource<IGraphicsSampler>> sampler) : GraphicsResourceDefinition<TrackedAsset>
    {
        public override async ValueTask<TrackedAsset> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            GraphicsResource<IGraphicsSampler> dependency = await context.RequireAsync(sampler, cancellationToken: cancellationToken);
            return new(Assert.IsType<TestSampler>(dependency.Value));
        }
    }

    private sealed class TrackedAsset(TestSampler sampler) : IDisposable
    {
        internal TestSampler Sampler { get; } = sampler;

        internal bool FailOnce { get; set; }

        internal bool Disposed { get; private set; }

        public void Dispose()
        {
            Assert.False(Sampler.Disposed);
            if (FailOnce)
            {
                FailOnce = false;
                throw new InvalidOperationException("Injected asset cleanup failure.");
            }

            Disposed = true;
        }
    }
}
