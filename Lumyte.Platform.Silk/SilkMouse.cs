using System.Numerics;

using Lumyte.Input;
using NativeMouse = Silk.NET.Input.IMouse;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkMouse : IMouse, IDisposable
{
    private readonly NativeMouse native;
    private Vector2 previousPosition;

    internal SilkMouse(NativeMouse native)
    {
        this.native = native;
        previousPosition = native.Position;
        native.MouseMove += OnMouseMove;
        native.MouseDown += OnMouseDown;
        native.MouseUp += OnMouseUp;
        native.Scroll += OnScroll;
    }

    public event EventHandler<MouseMovedEventArgs>? Moved;

    public event EventHandler<RawMouseMovedEventArgs>? RawMoved;

    public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged;

    public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;

    public Vector2 Position => native.Position;

    public NativeMouse Native => native;

    public bool IsCursorVisible
    {
        get => native.Cursor.CursorMode != Silk.NET.Input.CursorMode.Hidden;
        set
        {
            if (CursorMode == Lumyte.Input.CursorMode.Normal)
            {
                native.Cursor.CursorMode = value
                    ? Silk.NET.Input.CursorMode.Normal
                    : Silk.NET.Input.CursorMode.Hidden;
            }
        }
    }

    public Lumyte.Input.CursorMode CursorMode
    {
        get => native.Cursor.CursorMode switch
        {
            Silk.NET.Input.CursorMode.Raw => Lumyte.Input.CursorMode.Relative,
            _ when native.Cursor.IsConfined => Lumyte.Input.CursorMode.Confined,
            _ => Lumyte.Input.CursorMode.Normal,
        };
        set
        {
            native.Cursor.IsConfined = value == Lumyte.Input.CursorMode.Confined;
            native.Cursor.CursorMode = value == Lumyte.Input.CursorMode.Relative
                ? Silk.NET.Input.CursorMode.Raw
                : IsCursorVisible
                    ? Silk.NET.Input.CursorMode.Normal
                    : Silk.NET.Input.CursorMode.Hidden;
        }
    }

    public bool IsButtonPressed(MouseButton button) => native.SupportedButtons
        .Any(nativeButton => SilkInputConversions.ToLumyte(nativeButton) == button
            && native.IsButtonPressed(nativeButton));

    public void Dispose()
    {
        native.MouseMove -= OnMouseMove;
        native.MouseDown -= OnMouseDown;
        native.MouseUp -= OnMouseUp;
        native.Scroll -= OnScroll;
    }

    private void OnMouseMove(NativeMouse _, Vector2 position)
    {
        Vector2 delta = position - previousPosition;
        previousPosition = position;
        Moved?.Invoke(this, new(position, delta));
        if (CursorMode == Lumyte.Input.CursorMode.Relative)
        {
            RawMoved?.Invoke(this, new(delta));
        }
    }

    private void OnMouseDown(NativeMouse _, Silk.NET.Input.MouseButton button) =>
        ButtonChanged?.Invoke(this, new(SilkInputConversions.ToLumyte(button), true, Position));

    private void OnMouseUp(NativeMouse _, Silk.NET.Input.MouseButton button) =>
        ButtonChanged?.Invoke(this, new(SilkInputConversions.ToLumyte(button), false, Position));

    private void OnScroll(NativeMouse _, Silk.NET.Input.ScrollWheel wheel) =>
        WheelChanged?.Invoke(this, new(new(wheel.X, wheel.Y)));
}
