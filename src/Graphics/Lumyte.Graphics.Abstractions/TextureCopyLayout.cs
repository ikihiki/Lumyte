namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains backend-resolved copy constraints without padding allocation.</summary>
public sealed record TextureCopyLayout
{
    /// <summary>Gets the storage bytes per color texel.</summary>
    public required uint BytesPerTexel { get; init; }

    /// <summary>Gets the required byte offset alignment.</summary>
    public required ulong BufferOffsetAlignmentInBytes { get; init; }

    /// <summary>Gets the required row pitch alignment.</summary>
    public required uint BytesPerRowAlignment { get; init; }
}
