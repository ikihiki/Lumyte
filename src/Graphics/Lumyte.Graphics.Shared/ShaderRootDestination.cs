using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal readonly struct ShaderRootDestination(IShaderRootStorage storage) : IShaderTransferDestination
{
    public void Write(ReadOnlySpan<byte> bytes, int offset) => storage.Write(bytes, offset);
}
