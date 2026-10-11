namespace Lumyte.Graphics.Abstractions;

/// <summary>Requests an exact pixel size, format, usages and presentation policy.</summary>
public sealed record SwapchainDesc
{
    /// <summary>Gets the positive pixel width; zero-size targets are suspended by the caller.</summary>
    public required uint Width { get; init; }

    /// <summary>Gets the positive pixel height.</summary>
    public required uint Height { get; init; }

    /// <summary>Gets the requested surface-supported color format.</summary>
    public TextureFormat Format { get; init; } = TextureFormat.Bgra8Unorm;

    /// <summary>Gets the exact usages, including RenderAttachment.</summary>
    public TextureUsage Usage { get; init; } = TextureUsage.RenderAttachment;

    /// <summary>Gets the requested scheduling mode without silent fallback.</summary>
    public PresentMode PresentMode { get; init; } = PresentMode.Fifo;

    /// <summary>Gets the requested alpha composition mode.</summary>
    public SurfaceAlphaMode AlphaMode { get; init; } = SurfaceAlphaMode.Auto;
}
