namespace Lumyte.Graphics.Portable;

/// <summary>A texture region and its linear representation relative to the beginning of a buffer range.</summary>
/// <remarks>
/// Origin.Z and Extent.Depth select array layers for 2D textures or depth slices for 3D textures.
/// RowPitch and ImagePitch are byte strides; zero omits the stride from the backend copy descriptor.
/// Nonzero image pitch must be representable by that backend's row stride. Texture-to-texture copies ignore both pitches.
/// This describes transfer bytes, never the texture's internal memory placement.
/// </remarks>
public readonly record struct GpuTextureCopyFootprint(
    uint Mip,
    GpuTextureAspect Aspect,
    GpuOrigin3D Origin,
    GpuExtent3D Extent,
    ulong RowPitch = 0,
    ulong ImagePitch = 0)
{
    /// <summary>Calculates the covered byte span, including row/image gaps but excluding padding after the last row.</summary>
    /// <remarks>
    /// Omitted strides use the tightly packed byte count for this calculation; their GPU legality is not checked.
    /// The format is supplied explicitly because a footprint contains no resource or duplicate resource format.
    /// Depth24PlusStencil8 has a defined byte representation only for its stencil aspect.
    /// </remarks>
    public ulong RequiredBytes(GpuFormat format)
    {
        if (!Enum.IsDefined(Aspect)) { throw new InvalidOperationException("The footprint has an unknown texture aspect."); }
        uint bytesPerBlock = format switch
        {
            GpuFormat.R8Unorm => 1,
            GpuFormat.Rg8Unorm => 2,
            GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.Rgba8UnormSrgb
                or GpuFormat.Bgra8UnormSrgb or GpuFormat.R32Float or GpuFormat.D32Float => 4,
            GpuFormat.Depth24PlusStencil8 when Aspect == GpuTextureAspect.StencilOnly => 1,
            GpuFormat.Depth24PlusStencil8 => throw new NotSupportedException("This depth aspect has no defined Portable buffer byte representation."),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        return CalculateRequiredBytes(bytesPerBlock, 1, 1);
    }

    private ulong CalculateRequiredBytes(uint bytesPerBlock, uint blockWidth, uint blockHeight)
    {
        if (Extent.Width == 0 || Extent.Height == 0 || Extent.Depth == 0) { return 0; }
        checked
        {
            ulong columns = ((ulong)Extent.Width + blockWidth - 1) / blockWidth;
            ulong rows = ((ulong)Extent.Height + blockHeight - 1) / blockHeight;
            ulong lastRowBytes = columns * bytesPerBlock;
            ulong rowPitch = RowPitch == 0 ? lastRowBytes : RowPitch;
            ulong precedingImages = Extent.Depth == 1 ? 0
                : (ImagePitch == 0 ? rowPitch * rows : ImagePitch) * (Extent.Depth - 1);
            return precedingImages + rowPitch * (rows - 1) + lastRowBytes;
        }
    }
}
