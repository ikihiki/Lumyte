namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowState.</summary>
public sealed record WindowState
{
    /// <summary>Gets Id.</summary>
    public required WindowId Id { get; init; }

    /// <summary>Gets Owner.</summary>
    public required WindowId? Owner { get; init; }

    /// <summary>Gets Kind.</summary>
    public required WindowKind Kind { get; init; }

    /// <summary>Gets Activation.</summary>
    public required WindowActivation Activation { get; init; }

    /// <summary>Gets Title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets ClientSize.</summary>
    public required System.Numerics.Vector2 ClientSize { get; init; }

    /// <summary>Gets PixelSize.</summary>
    public required WindowPixelSize PixelSize { get; init; }

    /// <summary>Gets Scale.</summary>
    public required System.Numerics.Vector2 Scale { get; init; }

    /// <summary>Gets a value indicating whether IsVisible is enabled.</summary>
    public required bool IsVisible { get; init; }

    /// <summary>Gets a value indicating whether IsActive is enabled.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Gets DisplayMode.</summary>
    public required WindowDisplayMode DisplayMode { get; init; }

    /// <summary>Gets Lifetime.</summary>
    public required WindowLifetime Lifetime { get; init; }

    /// <summary>Gets Features.</summary>
    public required WindowFeatures Features { get; init; }
}
