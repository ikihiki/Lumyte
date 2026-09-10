namespace Lumyte.Graphics.Native.Tests.LinearData;

public sealed class NativeGpuRangeTests
{
    [Fact]
    public void GpuAddressUsesTheRegionBaseWithoutAddingHeapPlacement()
    {
        NativeGpuLinearRegion region = CreateRegion(size: 1024, gpuAddress: 0x8000, heapOffset: 65536);

        var range = new NativeGpuRange(region, 48, 16);

        Assert.Equal(0x8030ul, range.GpuAddress);
    }

    [Fact]
    public void NestedSlicesAccumulateOffsetsAndRetainTheirRegion()
    {
        NativeGpuLinearRegion region = CreateRegion(size: 1024, gpuAddress: 0x8000);
        var range = new NativeGpuRange(region, 128, 512);

        NativeGpuRange slice = range.Slice(64, 256).Slice(32, 16);

        Assert.Equal((region, 224ul, 16ul, 0x80E0ul), (slice.Region, slice.Offset, slice.Size, slice.GpuAddress));
    }

    [Fact]
    public void ConstructionRequiresARegion()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new NativeGpuRange(null!, 0, 0));

        Assert.Equal("region", exception.ParamName);
    }

    [Theory]
    [InlineData(64ul, 65ul, 0ul, "offset")]
    [InlineData(64ul, ulong.MaxValue, 1ul, "offset")]
    [InlineData(64ul, 0ul, 65ul, "size")]
    [InlineData(64ul, 32ul, 33ul, "size")]
    [InlineData(64ul, 1ul, ulong.MaxValue, "size")]
    [InlineData(ulong.MaxValue, ulong.MaxValue - 1, 3ul, "size")]
    public void ConstructionRejectsRangesOutsideTheRegion(ulong regionSize, ulong offset, ulong size, string parameterName)
    {
        NativeGpuLinearRegion region = CreateRegion(size: regionSize);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new NativeGpuRange(region, offset, size));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void RangeExcludesAllocationPadding()
    {
        var compatibility = new StubMemoryCompatibility();
        var requirements = new NativeGpuMemoryRequirements(65536, 65536, compatibility);
        var heap = new StubHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly);
        var region = new StubLinearRegion(heap, 0, 257, 0x8000);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new NativeGpuRange(region, 0, requirements.Size));

        Assert.Equal("size", exception.ParamName);
    }

    [Fact]
    public void ConstructionAllowsAnEmptyRangeAtTheRegionEnd()
    {
        NativeGpuLinearRegion region = CreateRegion(size: 64, gpuAddress: 0x8000);

        var range = new NativeGpuRange(region, 64, 0);

        Assert.Equal((64ul, 0ul, 0x8040ul), (range.Offset, range.Size, range.GpuAddress));
    }

    [Theory]
    [InlineData(33ul, 0ul, "offset")]
    [InlineData(ulong.MaxValue, 1ul, "offset")]
    [InlineData(0ul, 33ul, "size")]
    [InlineData(16ul, 17ul, "size")]
    [InlineData(1ul, ulong.MaxValue, "size")]
    public void SliceRejectsRangesOutsideItsSource(ulong offset, ulong size, string parameterName)
    {
        NativeGpuLinearRegion region = CreateRegion(size: 1024);
        var range = new NativeGpuRange(region, 64, 32);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => range.Slice(offset, size));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void SliceAllowsAnEmptyRangeAtItsSourceEnd()
    {
        NativeGpuLinearRegion region = CreateRegion(size: 1024, gpuAddress: 0x8000);
        var range = new NativeGpuRange(region, 64, 32);

        NativeGpuRange slice = range.Slice(32, 0);

        Assert.Equal((96ul, 0ul, 0x8060ul), (slice.Offset, slice.Size, slice.GpuAddress));
    }

    [Fact]
    public void SliceCanReachTheLargestUnsignedOffset()
    {
        NativeGpuLinearRegion region = CreateRegion(size: ulong.MaxValue, gpuAddress: 0);
        var range = new NativeGpuRange(region, ulong.MaxValue - 16, 16);

        NativeGpuRange slice = range.Slice(16, 0);

        Assert.Equal((ulong.MaxValue, 0ul, ulong.MaxValue), (slice.Offset, slice.Size, slice.GpuAddress));
    }

    [Fact]
    public void GpuAddressRejectsUnsignedOverflow()
    {
        NativeGpuLinearRegion region = CreateRegion(size: 64, gpuAddress: ulong.MaxValue - 3);
        var range = new NativeGpuRange(region, 4, 1);

        Assert.Throws<OverflowException>(() => range.GpuAddress);
    }

    [Fact]
    public void UninitializedRangeRejectsAddressAccess()
    {
        NativeGpuRange range = default;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => range.GpuAddress);

        Assert.Contains("uninitialized", exception.Message);
    }

    [Fact]
    public void UninitializedRangeRejectsSlicing()
    {
        NativeGpuRange range = default;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => range.Slice(0, 0));

        Assert.Contains("uninitialized", exception.Message);
    }

    private static NativeGpuLinearRegion CreateRegion(ulong size, ulong gpuAddress = 0x8000, ulong heapOffset = 0)
    {
        var heap = new StubHeap(checked(heapOffset + size), 1, NativeGpuMemoryKind.GpuOnly);
        return new StubLinearRegion(heap, heapOffset, size, gpuAddress);
    }

    private sealed class StubMemoryCompatibility : NativeGpuMemoryCompatibility;

    private sealed class StubHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind)
        : NativeGpuHeap(size, alignment, kind);

    private sealed class StubLinearRegion(NativeGpuHeap heap, ulong heapOffset, ulong size, ulong gpuAddress)
        : NativeGpuLinearRegion(heap, heapOffset, size, gpuAddress, 0);
}
