using System.Numerics;

namespace Lumyte.UI.Application.Desktop;

/// <summary>Configures the initially hidden desktop primary window.</summary>
public sealed record UiDesktopApplicationOptions
{
    /// <summary>Gets the title used by UseRoot or UseContent.</summary>
    public string Title { get; init; } = "Lumyte";

    /// <summary>Gets the initial logical client size.</summary>
    public Vector2 ClientSize { get; init; } = new(800, 600);
}
