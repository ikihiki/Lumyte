// Test-only streaming fake; never used by an engine backend.
namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Synchronously writes raw ranges into already owned final storage.</summary>
internal interface ITestShaderTransferDestination
{
    /// <summary>Copies a bounded raw segment without retaining its source.</summary>
    /// <param name="bytes">The current borrowed raw segment.</param>
    /// <param name="offset">The destination-relative byte position.</param>
    void Write(ReadOnlySpan<byte> bytes, int offset);
}
