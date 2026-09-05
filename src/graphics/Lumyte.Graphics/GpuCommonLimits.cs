namespace Lumyte.Graphics;

/// <summary>The guaranteed desktop backend profile. Native capabilities do not widen this contract.</summary>
public static class GpuCommonLimits
{
    public const int ColorAttachments = 1;
    public const int SampledTextures = 16;
    public const int Samplers = 16;
    public const int StorageBuffers = 8;
    public const int StorageTextures = 4;
    public const uint TextureDimension = 8192;
    public const ulong BufferSize = 256 * 1024 * 1024;
    public const uint DispatchGroups = 65535;

    public static void ValidateTexture(GpuTextureDescription description)
    {
        if (description.Width > TextureDimension || description.Height > TextureDimension)
        { throw new ArgumentOutOfRangeException(nameof(description), "Texture dimensions exceed the common limit of 8192."); }
        if (description.SampleCount != 1 || description.MipCount != 1 || description.LayerCount != 1)
        { throw new NotSupportedException("The common texture profile requires one sample, mip, and layer."); }
        if (!Enum.IsDefined(description.Format) || (description.Usage & ~(GpuTextureUsage)63) != 0)
        { throw new ArgumentOutOfRangeException(nameof(description)); }
        bool depth = GpuFormatInfo.IsDepthStencilAttachment(description.Format);
        if (depth && (description.Usage & (GpuTextureUsage.ColorAttachment | GpuTextureUsage.Storage | GpuTextureUsage.CopySource | GpuTextureUsage.CopyDestination)) != 0
            || !depth && (description.Usage & GpuTextureUsage.DepthStencilAttachment) != 0)
        { throw new NotSupportedException("The format and usage combination is outside the common texture profile."); }
        if ((description.Usage & GpuTextureUsage.Storage) != 0
            && description.Format is not (GpuFormat.Rgba8Unorm or GpuFormat.R32Float))
        { throw new NotSupportedException("Common storage textures use Rgba8Unorm or R32Float."); }
    }

    public static void ValidatePipeline(GpuRasterPipelineDescription description)
    {
        if (description.ColorTargets.Count != ColorAttachments || description.SampleCount != 1
            || description.AlphaToCoverage || description.SupportsDualSourceBlending)
        { throw new NotSupportedException("The common raster profile requires one color attachment, one sample, and no alpha-to-coverage or dual-source blending."); }
        if (description.DepthFormat is { } depth && description.StencilFormat is { } stencil && depth != stencil)
        { throw new NotSupportedException("Depth and stencil must use one attachment format."); }
    }
}
