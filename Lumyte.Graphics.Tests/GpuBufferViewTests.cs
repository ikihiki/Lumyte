using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuBufferViewTests
{
    [Fact]
    public void ZeroLengthSelectsTheRemainingBufferRange()
    {
        var buffer = new GpuBufferHandle(1, 64);

        GpuBufferViewDescription normalized = new GpuBufferViewDescription(16).Normalize(buffer);

        Assert.Equal(new GpuBufferViewDescription(16, 48), normalized);
    }

    [Theory]
    [InlineData(2, 16)]
    [InlineData(16, 6)]
    [InlineData(64, 0)]
    public void BufferViewRangeRequiresAlignedBytesInsideTheResource(ulong offset, ulong length)
    {
        var buffer = new GpuBufferHandle(1, 64);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new GpuBufferViewDescription(offset, length).Normalize(buffer));

        Assert.Equal("buffer", exception.ParamName);
    }
}
