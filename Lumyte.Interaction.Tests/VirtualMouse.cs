using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualMouse : IMouse
{
    private readonly HashSet<MouseButton> pressedButtons = [];

    public event EventHandler<MouseMovedEventArgs>? Moved;

    public event EventHandler<RawMouseMovedEventArgs>? RawMoved;

    public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    public Vector2 Position { get; private set; }

    public bool IsCursorVisible { get; set; } = true;

    public CursorMode CursorMode { get; set; }

    public bool IsButtonPressed(MouseButton button) => pressedButtons.Contains(button);

    public void Press(MouseButton button)
    {
        pressedButtons.Add(button);
        ButtonChanged?.Invoke(this, new(button, true, Position));
    }

    public void Release(MouseButton button)
    {
        pressedButtons.Remove(button);
        ButtonChanged?.Invoke(this, new(button, false, Position));
    }

    public void Move(Vector2 position)
    {
        Vector2 delta = position - Position;
        Position = position;
        Moved?.Invoke(this, new(position, delta));
    }

    public void MoveRaw(Vector2 delta) => RawMoved?.Invoke(this, new(delta));

    public void Scroll(Vector2 delta) => WheelChanged?.Invoke(this, new(delta));
}
