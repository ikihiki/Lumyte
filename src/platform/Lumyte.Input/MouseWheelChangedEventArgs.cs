using System.Numerics;

namespace Lumyte.Input;

public sealed class MouseWheelChangedEventArgs(Vector2 delta) : EventArgs
{
    public Vector2 Delta { get; } = delta;
}
