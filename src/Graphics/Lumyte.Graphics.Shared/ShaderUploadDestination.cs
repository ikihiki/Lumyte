using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal readonly struct ShaderUploadDestination(IGraphicsBuffer<byte> storage, ulong offset) : IShaderTransferDestination
{
    public void Write(ReadOnlySpan<byte> bytes, int relativeOffset)
        => storage.CopyBytesFrom(bytes, checked(offset + (ulong)relativeOffset), (ulong)bytes.Length);
}
