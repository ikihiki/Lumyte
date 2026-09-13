namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning, buffer-relative byte range. Null length means the remaining bytes in the resource.</summary>
public readonly record struct GpuBufferRange(GpuBufferHandle Buffer, ulong Offset = 0, ulong? Length = null)
{
    /// <summary>Resolves the length and checks host range arithmetic against caller-supplied creation values.</summary>
    /// <remarks>No device query, alignment check, usage validation, or resource lifetime validation is performed.</remarks>
    public GpuBufferRange Normalize(in GpuBufferDescription bufferDescription)
    {
        if (Offset > bufferDescription.Size)
        { throw new ArgumentOutOfRangeException(nameof(bufferDescription), "Offset exceeds the described buffer size."); }
        ulong length = Length ?? bufferDescription.Size - Offset;
        if (length > bufferDescription.Size - Offset)
        { throw new ArgumentOutOfRangeException(nameof(bufferDescription), "Length exceeds the described buffer's remaining bytes."); }
        return this with { Length = length };
    }

    /// <summary>Creates a subrange relative to this range; omitted length selects the remaining bytes.</summary>
    /// <remarks>The source length must be explicit or resolved by <see cref="Normalize"/> before slicing.</remarks>
    public GpuBufferRange Slice(ulong offset, ulong? length = null)
    {
        ulong available = Length ?? throw new InvalidOperationException("Normalize the buffer range or provide its length before slicing.");
        if (offset > available) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        ulong selectedLength = length ?? available - offset;
        if (selectedLength > available - offset) { throw new ArgumentOutOfRangeException(nameof(length)); }
        ulong selectedOffset = checked(Offset + offset);
        _ = checked(selectedOffset + selectedLength);
        return new(Buffer, selectedOffset, selectedLength);
    }
}
