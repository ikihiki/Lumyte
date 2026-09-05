using System.Numerics;

namespace Lumyte.Input;

public sealed class MouseButtonChangedEventArgs(MouseButton button, bool isPressed, Vector2 position) : EventArgs
{
    public MouseButton Button { get; } = button;

    public bool IsPressed { get; } = isPressed;

    public Vector2 Position { get; } = position;
}
