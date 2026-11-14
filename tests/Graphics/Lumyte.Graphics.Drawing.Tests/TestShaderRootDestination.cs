// Test-only streaming fake; never used by an engine backend.
namespace Lumyte.Graphics.Drawing.Tests;

internal readonly struct TestShaderRootDestination(ITestShaderRootStorage storage) : ITestShaderTransferDestination
{
    public void Write(ReadOnlySpan<byte> bytes, int offset) => storage.Write(bytes, offset);
}
