using System.Numerics;

namespace Lumyte.Input;

public interface IMouse
{
    event EventHandler<MouseMovedEventArgs>? Moved;

    event EventHandler<RawMouseMovedEventArgs>? RawMoved;

    event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    Vector2 Position { get; }

    bool IsCursorVisible { get; set; }

    CursorMode CursorMode { get; set; }

    bool IsButtonPressed(MouseButton button);
}
