using System.Numerics;

namespace Lumyte.Input;

public interface IMouse
{
    event EventHandler<MouseMovedEventArgs>? Moved;

    event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    Vector2 Position { get; }

    bool IsButtonPressed(MouseButton button);
}
