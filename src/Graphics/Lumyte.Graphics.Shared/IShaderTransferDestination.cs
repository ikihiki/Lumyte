namespace Lumyte.Graphics.Shared;

/// <summary>Synchronously writes raw ranges into already owned final storage.</summary>
internal interface IShaderTransferDestination
{
    /// <summary>Copies a bounded raw segment without retaining its source.</summary>
    /// <param name="bytes">The current borrowed raw segment.</param>
    /// <param name="offset">The destination-relative byte position.</param>
    void Write(ReadOnlySpan<byte> bytes, int offset);
}
