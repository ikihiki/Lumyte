using System.Numerics;

namespace Lumyte.UI.Application.Headless;

/// <summary>Configures the CPU-only virtual presentation root.</summary>
public sealed record UiHeadlessApplicationOptions
{
    /// <summary>Gets the logical client size.</summary>
    public Vector2 ClientSize { get; init; } = new(800, 600);

    /// <summary>Gets the per-axis scale.</summary>
    public Vector2 Scale { get; init; } = Vector2.One;
}
