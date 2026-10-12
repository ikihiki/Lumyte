using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

internal sealed record WindowNodeSnapshot
{
    public WindowNode? Parent { get; init; }

    public required WindowNode PresentationRoot { get; init; }

    public WindowNodeKind Kind { get; init; }

    public WindowRole Role { get; init; }

    public WindowOwnership Ownership { get; init; }

    public WindowId? NativeWindow { get; init; }

    public WindowRect Bounds { get; init; }

    public System.Numerics.Vector2 ClientSize { get; init; }

    public WindowPixelSize PixelSize { get; init; }

    public System.Numerics.Vector2 Scale { get; init; }

    public bool IsVisible { get; init; }

    public bool IsEffectivelyVisible { get; init; }

    public WindowLifetime Lifetime { get; init; }
}
