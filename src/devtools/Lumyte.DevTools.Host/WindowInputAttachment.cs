using System.Numerics;

using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.DevTools.Host;

internal sealed class WindowInputAttachment : IDisposable
{
    private readonly IWindow window; private readonly IKeyboard sourceKeyboard; private readonly IMouse sourceMouse; private readonly Action<string, string, object?, bool> changed; private bool disposed;
    public WindowInputAttachment(IWindow window, IWindowInput input, string id, Action<string, string, object?, bool> changed)
    {
        this.window = window;
        Id = id;
        this.changed = changed;
        sourceKeyboard = input.Keyboards[0];
        sourceMouse = input.Mice[0];
        Keyboard = new WindowKeyboard();
        Mouse = new WindowMouse();
        sourceKeyboard.KeyChanged += OnKey;
        sourceMouse.ButtonChanged += OnButton;
        sourceMouse.Moved += OnMoved;
        sourceMouse.RawMoved += OnRawMoved;
        sourceMouse.WheelChanged += OnWheel;
        window.FocusChanged += OnFocus;
    }
    public string Id { get; }
    public IKeyboard Keyboard { get; }
    public IMouse Mouse { get; }
    public RawInputSourceSnapshot Snapshot() => new("window", Id, ((WindowKeyboard)Keyboard).Pressed, ((WindowMouse)Mouse).Pressed, ((WindowMouse)Mouse).Position, ((WindowMouse)Mouse).Delta, ((WindowMouse)Mouse).Wheel);
    public void NeutralizeAxes() => ((WindowMouse)Mouse).Neutralize();
    public void Dispose() { if (disposed) { return; } disposed = true; sourceKeyboard.KeyChanged -= OnKey; sourceMouse.ButtonChanged -= OnButton; sourceMouse.Moved -= OnMoved; sourceMouse.RawMoved -= OnRawMoved; sourceMouse.WheelChanged -= OnWheel; window.FocusChanged -= OnFocus; Reset(); }
    private void OnKey(object? _, KeyChangedEventArgs e) { ((WindowKeyboard)Keyboard).Set(e.Key, e.IsPressed, e.IsRepeat); changed("keyboard", e.Key.ToString(), e.IsPressed, false); }
    private void OnButton(object? _, MouseButtonChangedEventArgs e) { ((WindowMouse)Mouse).SetButton(e.Button, e.IsPressed, e.Position); changed("mouse", e.Button.ToString(), e.IsPressed, false); }
    private void OnMoved(object? _, MouseMovedEventArgs e) { ((WindowMouse)Mouse).Move(e.Position, e.Delta); changed("mouse", "Delta", new { x = e.Delta.X, y = e.Delta.Y }, true); }
    private void OnRawMoved(object? _, RawMouseMovedEventArgs e) => ((WindowMouse)Mouse).Raw(e.Delta);
    private void OnWheel(object? _, MouseWheelChangedEventArgs e) { ((WindowMouse)Mouse).Scroll(e.Delta); changed("mouse", "Wheel", new { x = e.Delta.X, y = e.Delta.Y }, true); }
    private void OnFocus(object? _, WindowFocusChangedEventArgs e) { if (!e.IsFocused) { Reset(); changed("all", "focusLost", false, false); } }
    private void Reset() { ((WindowKeyboard)Keyboard).ReleaseAll(); ((WindowMouse)Mouse).ReleaseAll(); }
    private sealed class WindowKeyboard : IKeyboard { private readonly HashSet<Key> keys = []; public event EventHandler<KeyChangedEventArgs>? KeyChanged; public IReadOnlyList<string> Pressed => [.. keys.Order().Select(x => x.ToString())]; public bool IsKeyPressed(Key key) => keys.Contains(key); public void Set(Key key, bool value, bool repeat) { if (value) { keys.Add(key); } else { keys.Remove(key); } KeyChanged?.Invoke(this, new(key, value, repeat)); } public void ReleaseAll() { foreach (Key key in keys.ToArray()) { Set(key, false, false); } } }
    private sealed class WindowMouse : IMouse { private readonly HashSet<MouseButton> buttons = []; public event EventHandler<MouseMovedEventArgs>? Moved; public event EventHandler<RawMouseMovedEventArgs>? RawMoved; public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged; public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged; public Vector2 Position { get; private set; } public Vector2 Delta { get; private set; } public Vector2 Wheel { get; private set; } public IReadOnlyList<string> Pressed => [.. buttons.Order().Select(x => x.ToString())]; public bool IsCursorVisible { get; set; } = true; public CursorMode CursorMode { get; set; } public bool IsButtonPressed(MouseButton button) => buttons.Contains(button); public void SetButton(MouseButton button, bool value, Vector2 position) { Position = position; if (value) { buttons.Add(button); } else { buttons.Remove(button); } ButtonChanged?.Invoke(this, new(button, value, position)); } public void Move(Vector2 position, Vector2 delta) { Position = position; Delta = delta; Moved?.Invoke(this, new(position, delta)); } public void Raw(Vector2 delta) => RawMoved?.Invoke(this, new(delta)); public void Scroll(Vector2 delta) { Wheel = delta; WheelChanged?.Invoke(this, new(delta)); } public void Neutralize() { Delta = default; Wheel = default; } public void ReleaseAll() { foreach (MouseButton button in buttons.ToArray()) { SetButton(button, false, Position); } Neutralize(); } }
}
