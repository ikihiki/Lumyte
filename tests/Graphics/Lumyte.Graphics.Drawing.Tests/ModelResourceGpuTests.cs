using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics;
using Lumyte.Resources.Graphics.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks generic GPU resource definitions publish uploaded meshes and pin graph generations.</summary>
public sealed class ModelResourceGpuTests
{
    /// <summary>Checks GPU-only rebuilding reuses CPU streams and leaves existing graph leases pinned.</summary>
    /// <returns>The asynchronous integration check.</returns>
    [DrawingGpuFact]
    public async Task MeshDefinitionRebuildsAndPinsGenerationsAsync()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddGraphicsDevice<Factory>(ServiceLifetime.Singleton);
        var cpu = new ResourceKey<ModelMeshData>("model-cpu");
        var gpu = new ResourceKey<GraphicsResource<ModelMesh>>("model-gpu");
        services.AddGraphicsResource(gpu, new ModelMeshDefinition(cpu));
        services.AddResources(builder => builder.UseSource<UnusedSource>()
            .AddSourcePipeline<ModelMeshData, CpuStage>().AddGraphicsResourcePipeline<ModelMesh>());
        services.AddResourceGraphics();
        await using ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        await execution.StartAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        try
        {
            IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
            await devices.InitializeAsync();
            using GraphicsDeviceLease device = await devices.AcquireAsync();
            using ResourceReference<ModelMeshData> cpuReference = await manager.AcquireAsync(cpu);
            using ResourceLease<ModelMeshData> originalCpu = cpuReference.Acquire();
            using ResourceReference<GraphicsResource<ModelMesh>> reference = await manager.AcquireAsync(gpu);
            using var graph = new Graph(device.Device);
            GraphicsResource<ModelMesh> original = graph.ImportResources(manager).Acquire(reference);
            Assert.Equal(3u, original.Value.IndexCount);
            Assert.Equal(IndexFormat.Uint16, original.Value.IndexFormat);
            using (IGraphicsCommandBuffer unused = device.Device.CreateCommandBuffer(new()))
            {
                Assert.Throws<InvalidOperationException>(() => original.Value.RecordInitialUpload(unused));
            }

            Assert.Same(device.Device, original.Device);
            using CompiledRenderGraph plan = graph.Compile();
            GraphicsResourceRuntime runtime = provider.GetRequiredService<GraphicsResourceRuntime>();
            var lifetime = (GraphicsDeviceLifetime)typeof(VulkanDevice).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original.Device)!;
            lifetime.ReportLoss("TestLoss", "Simulated managed loss after completed upload; no native work is in flight.");
            await devices.WaitForRetirementAsync(original.Epoch);
            await devices.RecoverAsync();
            ResourceReloadResult result = await manager.RebuildRuntimeAsync([runtime.Id]);
            Assert.Equal(ResourceReloadStatus.Published, result.Status);
            using ResourceLease<GraphicsResource<ModelMesh>> current = reference.Acquire();
            Assert.NotSame(original.Value, current.Value.Value);
            Assert.Equal(3UL, original.Value.Vertices.Count);
            Assert.Equal(3UL, current.Value.Value.Vertices.Count);
            Assert.True(current.Value.Epoch > original.Epoch);
            Assert.NotSame(original.Device, current.Value.Device);
            using ResourceLease<ModelMeshData> cpuLease = cpuReference.Acquire();
            Assert.Same(originalCpu.Value, cpuLease.Value);
            Assert.Equal(3, cpuLease.Value.Indices16.Length);
        }
        finally
        {
            await manager.DisposeAsync();
            await execution.StopAsync();
        }
    }

    private sealed class CpuStage : IResourceSourceStage<ModelMeshData>
    {
        public ValueTask<ModelMeshData> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ModelVertex[] vertices =
            [
                new(Vector3.Zero, Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, Vector4.One),
                new(Vector3.UnitX, Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitX, Vector4.One),
                new(Vector3.UnitY, Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitY, Vector4.One),
            ];
            return new(new ModelMeshData(MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(), new ushort[] { 0, 1, 2 }));
        }

        public ValueTask ReleaseAsync(ModelMeshData resource) => ValueTask.CompletedTask;
    }

    private sealed class Factory : IGraphicsDeviceFactory
    {
        public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IGraphicsDeviceOwner>(new DeviceOwner(VulkanDevice.Create()));
    }

    private sealed class DeviceOwner(VulkanDevice device) : IGraphicsDeviceOwner
    {
        public IGraphicDevice Device => device;

        public ValueTask DisposeAsync()
        {
            device.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
