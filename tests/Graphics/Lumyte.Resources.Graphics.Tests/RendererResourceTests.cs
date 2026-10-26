using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics.Drawing2D;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Checks the public resource-to-renderer path without program maps or per-frame shader setup.</summary>
public sealed class RendererResourceTests
{
    /// <summary>Checks defaults do not read application sources and each renderer independently retains the shared program.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DefaultFactoriesShareResourcesAndOwnTheirLifetimesAsync()
    {
        var state = new GraphicsTestState();
        var source = new MutableShaderSource();
        await using ServiceProvider services = CreateServices(state, source, custom: false);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<Renderer2DResource> reference = await manager.AcquireRenderer2DAsync();
        using Renderer2D first = reference.CreateRenderer();
        using Renderer2D second = reference.CreateRenderer(DrawingCommandRenderers2D.CreateDefault());
        TestDevice device = Assert.Single(state.Devices);
        TestProgramPipeline pipeline = Assert.Single(device.Programs);
        Assert.Equal(0, source.Reads);
        reference.Dispose();
        first.Dispose();
        first.Dispose();
        await manager.TrimAsync();
        Assert.False(pipeline.Disposed);
        second.Dispose();
        await manager.TrimAsync();
        Assert.True(pipeline.Disposed);
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Checks custom inputs reload and old renderers stay pinned while newly created ones follow publication.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ReloadAndRecoveryReuseTheFactoryReferenceAsync()
    {
        var state = new GraphicsTestState();
        var source = new MutableShaderSource();
        await using ServiceProvider services = CreateServices(state, source, custom: true);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        IGraphicsDeviceService devices = services.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<Renderer2DResource> reference = await manager.AcquireRenderer2DAsync();
        ResourceHandle<Renderer2DResource> handle = reference.Handle;
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        using Renderer2D original = reference.CreateRenderer(snapshot: snapshot);
        var reloaded = new TaskCompletionSource<ResourceReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, args) => reloaded.TrySetResult(args.Result);
        manager.ReloadFailed += (_, args) => reloaded.TrySetException(args.Error);
        source.Change();
        Assert.Equal(ResourceReloadStatus.Published, (await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        TestDevice device = state.Devices[0];
        Assert.Equal(2, device.Programs.Count);
        int reads = source.Reads;
        using Renderer2D next = reference.CreateRenderer();
        using Renderer2D previousPublication = reference.CreateRenderer(snapshot: snapshot);
        Assert.Equal(reads, source.Reads);
        snapshot.Dispose();
        original.Dispose();
        await manager.TrimAsync();
        Assert.False(device.Programs[0].Disposed);
        previousPublication.Dispose();
        await manager.TrimAsync();
        Assert.True(device.Programs[0].Disposed);
        device.Lose();
        Assert.Throws<InvalidOperationException>(() => reference.CreateRenderer());
        next.Dispose();
        await devices.RecoverAsync();
        GraphicsResourceRuntime runtime = services.GetRequiredService<GraphicsResourceRuntime>();
        Assert.Equal(ResourceReloadStatus.Published, (await manager.RebuildRuntimeAsync([runtime.Id])).Status);
        using Renderer2D recovered = reference.CreateRenderer();
        Assert.Equal(handle, reference.Handle);
        Assert.Equal(reads, source.Reads);
        Assert.Single(state.Devices[1].Programs);
    }

    /// <summary>Checks explicit pipeline definitions override library defaults in either registration order.</summary>
    /// <param name="afterDefaults">Whether the application registers after library defaults.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineDefinitionOverridesDefaultsAndSharesSamplerAsync(bool afterDefaults)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(
            state,
            new MutableShaderSource(),
            custom: false,
            configure: collection => collection.AddGraphicsResource(DrawingGraphicsResources2D.Pipeline, new GraphicsPipelineDefinition
            {
                VertexShader = DrawingGraphicsResources2D.VertexShader,
                FragmentShader = DrawingGraphicsResources2D.FragmentShader,
                Optimization = PipelineOptimizationMode.FullSpecialization,
                Label = "configured-2d",
            }),
            afterDefaults: afterDefaults);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<Renderer2DResource> reference = await manager.AcquireRenderer2DAsync();
        using ResourceLease<Renderer2DResource> lease = reference.Acquire();
        DrawingResources2D resources = lease.Value.Resources;
        Assert.Equal("configured-2d", resources.Pipeline.Desc.Label);
        Assert.Equal(PipelineOptimizationMode.FullSpecialization, resources.Pipeline.Desc.Optimization);
        using ResourceReference<GraphicsResource<IGraphicsSampler>> sampler = await manager.AcquireAsync(DrawingGraphicsResources2D.Sampler);
        using ResourceLease<GraphicsResource<IGraphicsSampler>> samplerLease = sampler.Acquire();
        Assert.Same(samplerLease.Value.Value, resources.Sampler);
        using Renderer2D renderer = reference.CreateRenderer();
        Assert.Single(state.Devices[0].Programs);
    }

    private static ServiceProvider CreateServices(GraphicsTestState state, MutableShaderSource source, bool custom, Action<IServiceCollection>? configure = null, bool afterDefaults = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(source);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        if (custom)
        {
            services.UseGraphicsShaders<Renderer2DResource>(new("vertex"), new("fragment"));
        }

        if (!afterDefaults)
        {
            configure?.Invoke(services);
        }

        services.AddResources(builder => builder.UseSource<MutableShaderSource>()
            .AddShaderPipeline().AddGraphics2D()
            .ConfigureHotReload(new() { DebounceDelay = TimeSpan.Zero, MaximumBatchDelay = TimeSpan.FromMilliseconds(10) }));
        services.AddResourceGraphics();
        if (afterDefaults)
        {
            configure?.Invoke(services);
        }

        return services.BuildServiceProvider();
    }
}
