namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes explicit buffer row and layer storage for a texture copy.</summary>
public sealed record BufferTextureCopyLayout
{
    /// <summary>Gets the non-owning byte slice.</summary>
    public required BufferSlice<byte> Buffer { get; init; }

    /// <summary>Gets the caller-provided row pitch.</summary>
    public required uint BytesPerRow { get; init; }

    /// <summary>Gets the caller-provided layer pitch in rows.</summary>
    public required uint RowsPerImage { get; init; }
}
