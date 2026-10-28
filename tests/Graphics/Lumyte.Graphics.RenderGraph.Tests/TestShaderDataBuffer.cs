using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestShaderDataBuffer : IGraphicsShaderDataBuffer<TestShaderData>
{
    public ulong Count => 4;

    public ulong SizeInBytes => 16;

    public ulong ShaderElementStrideInBytes => 4;

    public MemoryPreference Memory => MemoryPreference.Automatic;

    public bool IsMapped => false;

    public void CopyFrom(ReadOnlySpan<TestShaderData> source, ulong elementOffset = 0) => throw new NotSupportedException();

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public ShaderDataSlice<TestShaderData> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void Dispose()
    {
    }
}
