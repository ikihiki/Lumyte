using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks combined allocation eligibility without allocating native resources.</summary>
public sealed class SharedStorageTests
{
    /// <summary>Large device buffers must still fit the signed CPU upload byte-offset range.</summary>
    [Fact]
    public void SharedStorageHonorsCpuUploadByteLimit()
    {
        ulong maximumInstructionCount = (ulong)int.MaxValue / 144;
        ulong maximumGeometryCount = (ulong)int.MaxValue / 16;
        Assert.True(PreparedGpuBatch2D.FitsSharedStorage(maximumInstructionCount, maximumGeometryCount, 144, ulong.MaxValue));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(maximumInstructionCount + 1, 1, 144, ulong.MaxValue));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(1, maximumGeometryCount + 1, 144, ulong.MaxValue));
        Assert.True(PreparedGpuBatch2D.FitsSharedStorage(1, maximumGeometryCount, 144, 1UL << 32));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(1, maximumGeometryCount + 1, 144, 1UL << 32));
    }

    /// <summary>Actual shader stride, geometry bytes and backend element limits bound sharing.</summary>
    [Fact]
    public void SharedStorageHonorsEveryAllocationLimit()
    {
        Assert.True(PreparedGpuBatch2D.FitsSharedStorage(10, 90, 144, 1440));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(11, 90, 144, 1440));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(10, 91, 144, 1440));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(10, 1, 256, 1440));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage((ulong)int.MaxValue + 1, 1, 1, ulong.MaxValue));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(1, ulong.MaxValue, 144, ulong.MaxValue));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(1, 1, 0, ulong.MaxValue));
        Assert.False(PreparedGpuBatch2D.FitsSharedStorage(int.MaxValue, 1, 144, ulong.MaxValue));
        Assert.True(PreparedGpuBatch2D.FitsSharedStorage(int.MaxValue, 1, 1, ulong.MaxValue));
    }
}
