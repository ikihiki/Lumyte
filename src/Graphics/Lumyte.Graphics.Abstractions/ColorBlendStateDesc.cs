namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes blending and exact channel writes.</summary>
public sealed record ColorBlendStateDesc
{
    /// <summary>Gets a value indicating whether blending is active.</summary>
    public bool BlendEnable { get; init; }

    /// <summary>Gets the color equation.</summary>
    public BlendComponentDesc Color { get; init; } = new();

    /// <summary>Gets the alpha equation.</summary>
    public BlendComponentDesc Alpha { get; init; } = new();

    /// <summary>Gets the channel mask; zero disables all writes.</summary>
    public ColorWriteMask WriteMask { get; init; } = ColorWriteMask.All;
}
