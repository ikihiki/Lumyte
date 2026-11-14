// Test-only streaming fake; never used by an engine backend.
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct TestShaderRootDestination(IShaderRootStorage storage) : ITestShaderTransferDestination
{
    public void Write(ReadOnlySpan<byte> bytes, int offset) => storage.Write(bytes, offset);
}
