using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12NativeMeshFeaturesTests
{
    [Fact]
    public void AbsentMeshTierDoesNotExposeMeshLimits()
    {
        Assert.Null(DirectX12Backend.MeshLimits(MeshShaderTier.TierNotSupported));
    }

    [Fact]
    public void MeshTierEnablesAmplificationWithNativeGridAndPayloadLimits()
    {
        var grid = new NativeGpuDispatchLimits(65535, 65535, 65535, 4194303);

        NativeGpuMeshShaderLimits? limits = DirectX12Backend.MeshLimits(MeshShaderTier.Tier1);

        Assert.Equal(new NativeGpuMeshShaderLimits(grid, grid, 256, 256, 16384), limits);
    }
}
