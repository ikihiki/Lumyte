namespace Lumyte.Graphics.Portable.Tests.Views;

public sealed class GpuBufferRangeTests
{
    [Fact]
    public void NormalizeResolvesTheRemainingBytesWithoutNarrowingTheOffset()
    {
        var buffer = new ExternalBuffer();
        ulong offset = (1ul << 40) + 13;
        var range = new GpuBufferRange(buffer, offset);

        GpuBufferRange normalized = range.Normalize(new(offset + 63, GpuBufferUsage.Storage));

        Assert.Equal(new GpuBufferRange(buffer, offset, 63), normalized);
        Assert.Null(range.Length);
    }

    [Fact]
    public void NormalizePreservesAnExplicitEmptyRange()
    {
        var range = new GpuBufferRange(new ExternalBuffer(), 16, 0);

        GpuBufferRange normalized = range.Normalize(new(64, GpuBufferUsage.Storage));

        Assert.Equal(range, normalized);
    }

    [Theory]
    [InlineData(65ul, null, "Offset")]
    [InlineData(1ul, ulong.MaxValue, "Length")]
    [InlineData(63ul, 2ul, "Length")]
    public void NormalizeRejectsHostRangesOutsideTheDescription(ulong offset, ulong? length, string field)
    {
        var range = new GpuBufferRange(new ExternalBuffer(), offset, length);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            range.Normalize(new(64, GpuBufferUsage.Storage)));

        Assert.Equal("bufferDescription", error.ParamName);
        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void SliceOffsetsAreRelativeToTheOriginalRange()
    {
        var buffer = new ExternalBuffer();
        ulong offset = (1ul << 40) + 13;
        var range = new GpuBufferRange(buffer, offset, 64);

        GpuBufferRange selected = range.Slice(8, 16);

        Assert.Equal(new GpuBufferRange(buffer, offset + 8, 16), selected);
    }

    [Theory]
    [InlineData(0ul, 32ul)]
    [InlineData(17ul, 15ul)]
    [InlineData(32ul, 0ul)]
    public void SliceWithoutLengthSelectsTheRemainingBytes(ulong offset, ulong length)
    {
        var buffer = new ExternalBuffer();
        var range = new GpuBufferRange(buffer, 64, 32);

        GpuBufferRange selected = range.Slice(offset);

        Assert.Equal(new GpuBufferRange(buffer, 64 + offset, length), selected);
    }

    [Fact]
    public void SliceRequiresTheSourceLengthToBeResolved()
    {
        var range = new GpuBufferRange(new ExternalBuffer(), 8);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => range.Slice(4, 8));

        Assert.Contains("Normalize", error.Message);
    }

    [Theory]
    [InlineData(33ul, null, "offset")]
    [InlineData(31ul, 2ul, "length")]
    [InlineData(1ul, ulong.MaxValue, "length")]
    public void SliceRejectsRangesOutsideTheSource(ulong offset, ulong? length, string parameter)
    {
        var range = new GpuBufferRange(new ExternalBuffer(), 64, 32);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => range.Slice(offset, length));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void SliceRejectsAnUnrepresentableBufferOffset()
    {
        var range = new GpuBufferRange(new ExternalBuffer(), ulong.MaxValue - 1, 8);

        Assert.Throws<OverflowException>(() => range.Slice(2, 1));
    }

    [Fact]
    public void SliceRejectsAnUnrepresentableRangeEnd()
    {
        var range = new GpuBufferRange(new ExternalBuffer(), ulong.MaxValue - 4, 16);

        Assert.Throws<OverflowException>(() => range.Slice(0, 8));
    }

    [Fact]
    public void SliceCanSelectARepresentablePrefixWithoutNormalizingTheWholeSource()
    {
        var buffer = new ExternalBuffer();
        var range = new GpuBufferRange(buffer, ulong.MaxValue - 4, 16);

        GpuBufferRange selected = range.Slice(0, 4);

        Assert.Equal(new GpuBufferRange(buffer, ulong.MaxValue - 4, 4), selected);
    }

    private sealed class ExternalBuffer : GpuBufferHandle;
}
