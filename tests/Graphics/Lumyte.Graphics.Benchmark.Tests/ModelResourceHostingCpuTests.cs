using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;
using Lumyte.Graphics.Drawing.Benchmark.Models;
using Lumyte.Graphics.Hosting.Benchmark.Devices;
using Lumyte.Resources.Graphics.Benchmark.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Deterministic public model/resource/hosting workload checks; no timing or allocation thresholds.</summary>
public sealed class ModelResourceHostingCpuTests
{
    /// <summary>Actual indexed draws upload the full bulk argument records and preserve bounded ownership.</summary>
    [Fact]
    public void ModelIndexedInstancesRecordAndRelease()
    {
        using var workload = new ModelWorkload();
        long retained = workload.Device.Counters.LiveResources;
        var world = Matrix4x4.CreateScale(2, 4, 8);
        Matrix4x4 normal = ModelDrawing.CreateNormalTransform(world);
        Assert.Equal(0.5f, normal.M11);
        Assert.Equal(0.25f, normal.M22);
        Assert.Equal(0.125f, normal.M33);
        GraphicsRenderStateDesc blend = ModelDrawing.CreateRenderState(ModelDrawing.DefaultMaterial with { AlphaMode = 2 }, doubleSided: true, mirrored: true);
        Assert.False(blend.DepthStencil.DepthWriteEnable);
        Assert.True(blend.DepthStencil.DepthTestEnable);
        Assert.Equal(CullMode.None, blend.Rasterization.Cull);
        Assert.Equal(FrontFace.Clockwise, blend.Rasterization.FrontFace);
        Assert.True(blend.ColorTargets[0].BlendEnable);
        GraphicsRenderStateDesc opaque = ModelDrawing.CreateRenderState(ModelDrawing.DefaultMaterial);
        Assert.True(opaque.DepthStencil.DepthWriteEnable);
        Assert.False(opaque.ColorTargets[0].BlendEnable);
        workload.RecordInstances(32);
        Assert.Equal(32, workload.Device.Counters.Draws);
        Assert.Equal(1, workload.Device.Counters.RenderPasses);
        Assert.Equal(32L * TransferSize<ModelDrawArguments>(), workload.Device.Counters.UploadedBytes);
        Assert.Equal(0, workload.Device.Counters.BufferCreates);
        Assert.Equal(1, workload.Device.Counters.Copies);
        Assert.Equal(2, workload.Device.Counters.BarrierCalls);
        Assert.Equal(retained, workload.Device.Counters.LiveResources);
        Assert.NotEqual(0UL, workload.Device.Counters.Checksum);
        workload.CreateUploadDispose(false);
        Assert.Equal(4, workload.Device.Counters.BufferCreates);
        Assert.Equal(2, workload.Device.Counters.Copies);
        Assert.Equal(4, workload.Device.Counters.BarrierCalls);
        Assert.Equal(200, workload.Device.Counters.UploadedBytes);
        Assert.Equal(1, workload.Device.Counters.Submissions);
        Assert.Equal(retained, workload.Device.Counters.LiveResources);
        workload.CreateUploadDispose(true);
        Assert.Equal(2, workload.Device.Counters.Copies);
        Assert.Equal(retained, workload.Device.Counters.LiveResources);
    }

    /// <summary>Finite cold waves really reopen/decode/upload, while retained warm pinning does none.</summary>
    /// <returns>Completion of the deterministic resource waves.</returns>
    [Fact]
    public async Task ResourceColdWarmResidencyAndConsumerDiAsync()
    {
        await using (var cold = new ResourceModelWorkload(false))
        {
            for (int wave = 1; wave <= 3; wave++)
            {
                Assert.NotEqual(0UL, await cold.ColdDecodeUploadTrimAsync());
                Assert.Equal(wave, cold.Source.Opens);
                Assert.Equal(wave, cold.Stage.Builds);
                Assert.Equal(wave, cold.Stage.Releases);
                Assert.Equal(4, cold.Device.Counters.BufferCreates);
                Assert.Equal(2, cold.Device.Counters.Copies);
                Assert.Equal(1, cold.Device.Counters.Submissions);
                Assert.Equal(0, cold.Device.Counters.LiveResources);
            }
        }

        await using var warm = new ResourceModelWorkload(true);
        for (int wave = 0; wave < 3; wave++)
        {
            Assert.NotEqual(0UL, await warm.RetainPinImportAsync());
            Assert.Equal(1, warm.Source.Opens);
            Assert.Equal(1, warm.Stage.Builds);
            Assert.Equal(0, warm.Device.Counters.BufferCreates);
            Assert.Equal(0, warm.Device.Counters.UploadedBytes);
            Assert.Equal(2, warm.Device.Counters.LiveResources);
        }

        Assert.True(await warm.RendererResourceAsync() > 0);
        Assert.True(await warm.EffectResourceAsync() > 0);
        Assert.True(await warm.GraphRendererResourceAsync() > 0);
        for (int wave = 0; wave < 3; wave++)
        {
            Assert.NotEqual(0UL, await warm.RebuildGraphicsAsync());
            Assert.Equal(1, warm.Source.Opens);
            Assert.Equal(1, warm.Stage.Builds);
        }
    }

    /// <summary>Public hosting registration owns a real managed device and returns every exact-epoch lease.</summary>
    /// <returns>Completion of owner lifetime verification.</returns>
    [Fact]
    public async Task HostingOwnerAndLeaseLifetimeAsync()
    {
        var benchmark = new GraphicsDeviceServiceBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Equal(1UL, benchmark.AcquireUseRelease());
            Assert.True(await benchmark.RegisterInitializeDisposeAsync() > 1);
            Assert.Equal(1UL, benchmark.AcquireUseRelease());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    private static int TransferSize<T>()
        where T : struct, IShaderArguments => T.TransferSize;
}
