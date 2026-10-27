using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics;
using Lumyte.Resources.Graphics.Drawing2D;
using Lumyte.Resources.Graphics.ImageEffects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks real GPU drawing and effect readback through resource-manager definitions and shared native programs.</summary>
public sealed class ManagedGraphicsGpuTests
{
    /// <summary>Checks Vulkan uses configured shared pipelines after renderer disposal and across effect graphs.</summary>
    /// <returns>The GPU check.</returns>
    [GpuFact]
    public async Task VulkanDefinedResourcesReadbackAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAsync(device);
    }

    /// <summary>Checks wgpu uses configured shared pipelines after renderer disposal and across effect graphs.</summary>
    /// <returns>The GPU check.</returns>
    [GpuFact]
    public async Task WgpuDefinedResourcesReadbackAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAsync(device);
    }

    private static async Task CheckAsync(IGraphicDevice device)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton(device);
        registrations.AddMemoryManagement();
        registrations.AddGraphicsDevice<BorrowedFactory>(ServiceLifetime.Singleton);
        registrations.AddGraphicsResource(DrawingGraphicsResources2D.Pipeline, new GraphicsPipelineDefinition
        {
            VertexShader = DrawingGraphicsResources2D.VertexShader,
            FragmentShader = DrawingGraphicsResources2D.FragmentShader,
            Optimization = PipelineOptimizationMode.FullSpecialization,
        });
        registrations.AddGraphicsResource(ImageEffectGraphicsResources.Pipeline, new GraphicsPipelineDefinition
        {
            VertexShader = ImageEffectGraphicsResources.VertexShader,
            FragmentShader = ImageEffectGraphicsResources.FragmentShader,
            Optimization = PipelineOptimizationMode.FullSpecialization,
        });
        registrations.AddResources(builder => builder.UseSource<UnusedSource>().AddGraphics2D().AddImageEffects());
        registrations.AddResourceGraphics();
        await using ServiceProvider provider = registrations.BuildServiceProvider();
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 1, new());
        await execution.StartAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        try
        {
            await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
            using ResourceReference<Renderer2DResource> reference = await manager.AcquireRenderer2DAsync();
            using Renderer2D renderer = reference.CreateRenderer();
            reference.Dispose();
            await Drawing2DTests.CheckStandaloneAsync(device, renderer, manager);
            using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder().AddResources(manager).AddImageEffects(manager).BuildAsync();
            foreach (float opacity in new[] { 0.25f, 0.5f })
            {
                var effect = ImageEffectGraph.Create(new OpacityEffect(opacity), ImageEffectGraph.Source());
                (_, Vector4[] pixels) = await ImageEffectsTests.RenderAsync(device, effect, new(0, 0, 1, 1), Vector4.One, extensions);
                Assert.True(Vector4.Distance(new(opacity), Assert.Single(pixels)) < 0.002f);
            }
        }
        finally
        {
            await manager.DisposeAsync();
            await execution.StopAsync();
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Embedded definitions must not read application sources.");

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new((string?)null);
    }

    private sealed class BorrowedFactory(IGraphicDevice device) : IGraphicsDeviceFactory
    {
        public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default) => new(new BorrowedOwner(device));
    }

    private sealed class BorrowedOwner(IGraphicDevice device) : IGraphicsDeviceOwner
    {
        public IGraphicDevice Device => device;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
