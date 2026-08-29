using System.Drawing;

namespace Lumyte.Platform;

public sealed record WindowOptions
{
    public string Title { get; init; } = "Lumyte";

    public Size ClientSize { get; init; } = new(1280, 720);

    public Point? Position { get; init; }

    public WindowState State { get; init; } = WindowState.Normal;

    public bool IsVisible { get; init; } = true;
}
