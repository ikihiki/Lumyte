using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

/// <summary>Defines WindowNodeState.</summary>
public sealed record WindowNodeState
{
    /// <summary>Gets Parent.</summary>
    public WindowNode? Parent { get; init; }

    /// <summary>Gets PresentationRoot.</summary>
    public required WindowNode PresentationRoot { get; init; }

    /// <summary>Gets Kind.</summary>
    public WindowNodeKind Kind { get; init; }

    /// <summary>Gets Role.</summary>
    public WindowRole Role { get; init; }

    /// <summary>Gets Ownership.</summary>
    public WindowOwnership Ownership { get; init; }

    /// <summary>Gets NativeWindow.</summary>
    public WindowId? NativeWindow { get; init; }

    /// <summary>Gets Bounds.</summary>
    public WindowRect Bounds { get; init; }

    /// <summary>Gets ClientSize.</summary>
    public System.Numerics.Vector2 ClientSize { get; init; }

    /// <summary>Gets PixelSize.</summary>
    public WindowPixelSize PixelSize { get; init; }

    /// <summary>Gets Scale.</summary>
    public System.Numerics.Vector2 Scale { get; init; }

    /// <summary>Gets a value indicating whether IsVisible is enabled.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Gets a value indicating whether IsEffectivelyVisible is enabled.</summary>
    public bool IsEffectivelyVisible { get; init; }

    /// <summary>Gets Lifetime.</summary>
    public WindowLifetime Lifetime { get; init; }
}
