using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12NativeBarrierTests
{
    [Theory]
    [InlineData(GpuStage.AmplificationShader | GpuStage.MeshShader, BarrierSync.VertexShading)]
    [InlineData(GpuStage.AmplificationShader, BarrierSync.VertexShading)]
    [InlineData(GpuStage.MeshShader, BarrierSync.VertexShading)]
    [InlineData(GpuStage.IndexInput | GpuStage.DrawIndirect, BarrierSync.IndexInput | BarrierSync.ExecuteIndirect)]
    [InlineData(GpuStage.Copy | GpuStage.ComputeShader, BarrierSync.Copy | BarrierSync.ComputeShading)]
    [InlineData(GpuStage.AllGraphics, BarrierSync.Draw)]
    [InlineData(GpuStage.Host, BarrierSync.All)]
    public void StageScopesPreserveTheRequestedOperations(GpuStage input, BarrierSync expected)
    {
        Assert.Equal(expected, DirectX12Backend.BarrierStages(input));
    }

    [Theory]
    [InlineData(GpuAccess.CopyRead, BarrierAccess.CopySource)]
    [InlineData(GpuAccess.CopyWrite, BarrierAccess.CopyDest)]
    [InlineData(GpuAccess.IndexRead | GpuAccess.IndirectRead, BarrierAccess.IndexBuffer | BarrierAccess.IndirectArgument)]
    [InlineData(GpuAccess.HostRead, BarrierAccess.Common)]
    [InlineData(GpuAccess.HostWrite, BarrierAccess.NoAccess)]
    [InlineData(GpuAccess.HostWrite | GpuAccess.CopyWrite, BarrierAccess.CopyDest)]
    [InlineData(GpuAccess.None, BarrierAccess.NoAccess)]
    [InlineData(GpuAccess.DescriptorRead, BarrierAccess.NoAccess)]
    public void AccessScopesPreserveTheRequestedOperations(GpuAccess input, BarrierAccess expected)
    {
        Assert.Equal(expected, DirectX12Backend.BarrierAccesses(input));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownScopeBitsAreNotSilentlyDiscarded(bool access)
    {
        ArgumentOutOfRangeException error = access
            ? Assert.Throws<ArgumentOutOfRangeException>(() => DirectX12Backend.BarrierAccesses((GpuAccess)(1u << 31)))
            : Assert.Throws<ArgumentOutOfRangeException>(() => DirectX12Backend.BarrierStages((GpuStage)(1u << 31)));

        Assert.Equal(access ? "access" : "stages", error.ParamName);
    }

    [Fact]
    public void CopyCapacityIncludesImageGapsAndExcludesFinalRowPadding()
    {
        var copy = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color, 0, 2,
            default, new(3, 2, 1), 256, 2048);

        ulong required = DirectX12Backend.CopyByteCount(copy, 2, 4);

        Assert.Equal(2316ul, required);
    }

    [Fact]
    public void CopyCapacityOverflowDoesNotWrapToASmallRange()
    {
        var copy = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color, 0, 3,
            default, new(1, 1, 1), 256, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => DirectX12Backend.CopyByteCount(copy, 3, 4));
    }

    [Theory]
    [InlineData(GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Depth)]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Color)]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Stencil)]
    public void IncompatibleAspectsAreRejectedBeforeTheirMeaningBecomesAPlaneIndex(GpuFormat format, NativeGpuTextureAspect aspect)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.TexturePlanes(format, aspect));

        Assert.Equal("aspect", error.ParamName);
    }
}
