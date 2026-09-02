using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuShaderBinaryTests
{
    [Fact]
    public void ConstructorPreservesBackendReadyBytes()
    {
        byte[] bytes = [1, 2, 3];
        byte[] abi = new byte[32];

        var binary = new GpuShaderBinary(bytes, GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", abi);

        Assert.True(binary.Bytes.Span.SequenceEqual(bytes));
        Assert.Equal(GpuShaderStage.Vertex, binary.Stage);
    }

    [Fact]
    public void ConstructorRejectsEmptyIr()
    {
        Assert.Throws<ArgumentException>(() => new GpuShaderBinary(
            ReadOnlyMemory<byte>.Empty, GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", new byte[32]));
    }
}
