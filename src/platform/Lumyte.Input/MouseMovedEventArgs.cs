using System.Numerics;

namespace Lumyte.Input;

public sealed class MouseMovedEventArgs(Vector2 position, Vector2 delta) : EventArgs
{
    public Vector2 Position { get; } = position;

    public Vector2 Delta { get; } = delta;
}
