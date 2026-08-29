using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Benchmarks;

internal sealed class BenchmarkMouse : IMouse
{
    public event EventHandler<MouseMovedEventArgs>? Moved;

    public event EventHandler<RawMouseMovedEventArgs>? RawMoved
    {
        add { }
        remove { }
    }

    public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged
    {
        add { }
        remove { }
    }

    public Vector2 Position { get; private set; }

    public bool IsCursorVisible { get; set; } = true;

    public CursorMode CursorMode { get; set; }

    public bool IsButtonPressed(MouseButton button) => false;

    public void Move(Vector2 delta)
    {
        Position += delta;
        Moved?.Invoke(this, new(Position, delta));
    }
}
