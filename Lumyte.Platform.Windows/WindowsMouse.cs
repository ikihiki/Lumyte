using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsMouse : IMouse
{
    private readonly HashSet<MouseButton> pressedButtons = [];

    public event EventHandler<MouseMovedEventArgs>? Moved;

    public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    public Vector2 Position { get; private set; }

    public bool IsButtonPressed(MouseButton button) => pressedButtons.Contains(button);

    internal void Move(Vector2 position)
    {
        Vector2 delta = position - Position;
        Position = position;
        Moved?.Invoke(this, new(position, delta));
    }

    internal void ChangeButton(MouseButton button, bool isPressed, Vector2 position)
    {
        Position = position;
        if (isPressed)
        {
            pressedButtons.Add(button);
        }
        else
        {
            pressedButtons.Remove(button);
        }

        ButtonChanged?.Invoke(this, new(button, isPressed, position));
    }

    internal void ChangeWheel(Vector2 delta) => WheelChanged?.Invoke(this, new(delta));
}
