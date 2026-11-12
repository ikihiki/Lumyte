using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class ConsumerLinkBuffer(ShaderArtifact artifact, ulong count = 1, MemoryPreference memory = MemoryPreference.Automatic) : IGraphicsShaderDataBuffer<ConsumerLink>, IShaderDataSource
{
    private readonly ShaderValueSnapshot?[] _values = new ShaderValueSnapshot?[checked((int)count)];
    private bool _disposed;

    public ulong Count => count;

    public ulong SizeInBytes => count * 16;

    public ulong ShaderElementStrideInBytes => 16;

    public MemoryPreference Memory => memory;

    public bool IsMapped => false;

    public IShaderDataLayout Layout => ShaderDataLayout.Root(artifact.GetTarget(ShaderTarget.SpirV), "arguments");

    public object ShaderHandle => this;

    public void CopyFrom(ReadOnlySpan<ConsumerLink> source, ulong elementOffset = 0) => throw new NotSupportedException();

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public ShaderDataSlice<ConsumerLink> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public ShaderValueSnapshot Read(ulong index)
    {
        ValidateAlive();
        return _values[checked((int)index)] ?? new("element", "ConsumerLink", []);
    }

    public void SetTransferredValue(ulong index, ShaderValueSnapshot value) => _values[checked((int)index)] = value;

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => _disposed = true;
}
