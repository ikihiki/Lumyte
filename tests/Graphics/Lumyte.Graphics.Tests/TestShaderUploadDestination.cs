// Test-only streaming fake; never used by an engine backend.
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal readonly struct TestShaderUploadDestination(IGraphicsBuffer<byte> storage, ulong offset) : ITestShaderTransferDestination
{
    public void Write(ReadOnlySpan<byte> bytes, int relativeOffset)
        => storage.CopyBytesFrom(bytes, checked(offset + (ulong)relativeOffset), (ulong)bytes.Length);
}
