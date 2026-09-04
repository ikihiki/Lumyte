namespace Lumyte.Graphics;

public enum GpuFormat
{
    Rgba8Unorm,
    Bgra8Unorm,
    R32Float,
    D32Float,
    Rgba8UnormSrgb,
    Bgra8UnormSrgb,
    R8Unorm,
    Rg8Unorm,
    Depth24PlusStencil8,
}

internal static class GpuFormatInfo
{
    internal static uint BytesPerPixel(GpuFormat format) => format switch
    {
        GpuFormat.R8Unorm => 1,
        GpuFormat.Rg8Unorm => 2,
        GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm
            or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb
            or GpuFormat.R32Float or GpuFormat.D32Float => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static bool IsSampledUpload(GpuFormat format) => IsColor(format);

    internal static bool IsPortableSampled(GpuFormat format) => format is
        GpuFormat.R8Unorm or GpuFormat.Rg8Unorm
        or GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm
        or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb;

    internal static bool HasDepth(GpuFormat format) => format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8;
    internal static bool HasStencil(GpuFormat format) => format == GpuFormat.Depth24PlusStencil8;
    internal static bool IsDepthStencilAttachment(GpuFormat format) => HasDepth(format) || HasStencil(format);
    internal static bool IsColor(GpuFormat format) => Enum.IsDefined(format) && !IsDepthStencilAttachment(format);
}
