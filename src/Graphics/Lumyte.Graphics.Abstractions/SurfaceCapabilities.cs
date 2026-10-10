namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains a snapshot of this surface's capabilities on its selected device.</summary>
public sealed record SurfaceCapabilities
{
    /// <summary>Gets the supported color formats, in backend preference order.</summary>
    public required IReadOnlyList<TextureFormat> Formats { get; init; }

    /// <summary>Gets the supported scheduling policies.</summary>
    public required IReadOnlyList<PresentMode> PresentModes { get; init; }

    /// <summary>Gets the supported alpha composition modes.</summary>
    public required IReadOnlyList<SurfaceAlphaMode> AlphaModes { get; init; }

    /// <summary>Gets the permitted image usages.</summary>
    public required TextureUsage SupportedUsage { get; init; }

    /// <summary>Gets the minimum pixel width.</summary>
    public uint MinWidth { get; init; } = 1;

    /// <summary>Gets the minimum pixel height.</summary>
    public uint MinHeight { get; init; } = 1;

    /// <summary>Gets the maximum pixel width.</summary>
    public required uint MaxWidth { get; init; }

    /// <summary>Gets the maximum pixel height.</summary>
    public required uint MaxHeight { get; init; }

    /// <summary>Gets the required current width, or null when the caller chooses within the limits.</summary>
    public uint? CurrentWidth { get; init; }

    /// <summary>Gets the required current height, or null when the caller chooses within the limits.</summary>
    public uint? CurrentHeight { get; init; }
}
