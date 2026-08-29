using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsMouse : IMouse, IDisposable
{
    private readonly HashSet<MouseButton> pressedButtons = [];
    private readonly WindowsWindow? window;
    private bool isCursorVisible = true;
    private CursorMode cursorMode;

    public WindowsMouse()
    {
    }

    internal WindowsMouse(WindowsWindow window)
    {
        this.window = window;
    }

    public event EventHandler<MouseMovedEventArgs>? Moved;

    public event EventHandler<RawMouseMovedEventArgs>? RawMoved;

    public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    public Vector2 Position { get; private set; }

    public bool IsCursorVisible
    {
        get => isCursorVisible;
        set
        {
            if (isCursorVisible == value)
            {
                return;
            }

            isCursorVisible = value;
            ApplyCursorState();
        }
    }

    public CursorMode CursorMode
    {
        get => cursorMode;
        set
        {
            if (cursorMode == value)
            {
                return;
            }

            cursorMode = value;
            ApplyCursorState();
        }
    }

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

    internal void MoveRaw(Vector2 delta)
    {
        if (delta != Vector2.Zero)
        {
            RawMoved?.Invoke(this, new(delta));
        }
    }

    internal void UpdateCursorState() => ApplyCursorState();

    public void Dispose()
    {
        cursorMode = CursorMode.Normal;
        isCursorVisible = true;
        if (window is not null)
        {
            WindowsCursor.Release();
        }
    }

    private void ApplyCursorState()
    {
        if (window is null || window.Handle == 0)
        {
            return;
        }

        bool isActive = window.IsFocused;
        WindowsCursor.SetVisible(!isActive || (isCursorVisible && cursorMode != CursorMode.Relative));
        WindowsCursor.SetConfinement(isActive && cursorMode != CursorMode.Normal ? window.Handle : 0);
        WindowsCursor.EnableRawInput(window.Handle, cursorMode == CursorMode.Relative);
    }
}
