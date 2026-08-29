using System.Drawing;

namespace Lumyte.Platform;

public sealed class WindowMovedEventArgs(Point position) : EventArgs
{
    public Point Position { get; } = position;
}
