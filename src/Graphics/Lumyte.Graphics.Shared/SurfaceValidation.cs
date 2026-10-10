using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Validates exact swapchain configurations without backend fallback.</summary>
public static class SurfaceValidation
{
    /// <summary>Checks dimensions, color format, usage and surface-specific support.</summary>
    /// <param name="desc">The immutable requested configuration.</param>
    /// <param name="caps">The current surface support snapshot.</param>
    public static void Validate(SwapchainDesc desc, SurfaceCapabilities caps)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(caps);
        const TextureUsage KnownUsage = TextureUsage.RenderAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled;
        if (desc.Width == 0 || desc.Height == 0 || !Enum.IsDefined(desc.Format) ||
            !Enum.IsDefined(desc.PresentMode) || !Enum.IsDefined(desc.AlphaMode) ||
            (desc.Usage & TextureUsage.RenderAttachment) == 0 || (desc.Usage & ~KnownUsage) != 0)
        {
            throw new ArgumentException("Invalid swapchain size, format, usage or mode.", nameof(desc));
        }

        if (desc.Width < caps.MinWidth || desc.Height < caps.MinHeight || desc.Width > caps.MaxWidth || desc.Height > caps.MaxHeight ||
            (caps.CurrentWidth is { } width && width != desc.Width) || (caps.CurrentHeight is { } height && height != desc.Height))
        {
            throw new ArgumentException("Swapchain size conflicts with current surface limits.", nameof(desc));
        }

        if (!caps.Formats.Contains(desc.Format) || !caps.PresentModes.Contains(desc.PresentMode) ||
            !caps.AlphaModes.Contains(desc.AlphaMode) || (desc.Usage & ~caps.SupportedUsage) != 0)
        {
            throw new NotSupportedException("The surface does not support the exact requested format, usage or mode.");
        }
    }
}
