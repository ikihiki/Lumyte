using Lumyte.Input;
using Lumyte.Platform;
using NativeInputContext = Silk.NET.Input.IInputContext;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkWindowInput : IWindowInput, IDisposable
{
    private readonly NativeInputContext native;
    private readonly IReadOnlyList<IKeyboard> keyboards;
    private readonly IReadOnlyList<IMouse> mice;
    private readonly IReadOnlyList<ITouchscreen> touchscreens = [];
    private readonly Dictionary<Silk.NET.Input.IGamepad, SilkGamepad> gamepads = [];
    private bool disposed;

    internal SilkWindowInput(
        SilkWindow window,
        NativeInputContext native)
    {
        Window = window;
        this.native = native;
        Keyboards = [.. native.Keyboards.Select(keyboard => new SilkKeyboard(keyboard))];
        Mice = [.. native.Mice.Select(mouse => new SilkMouse(mouse))];
        keyboards = Keyboards;
        mice = Mice;
        foreach (Silk.NET.Input.IGamepad gamepad in native.Gamepads)
        {
            gamepads.Add(gamepad, new(gamepad));
        }

        TextInput = new SilkTextInputContext(Keyboards);
        native.ConnectionChanged += OnConnectionChanged;
    }

    internal event EventHandler<GamepadConnectionChangedEventArgs>? GamepadConnectionChanged;

    public SilkWindow Window { get; }

    public NativeInputContext Native => native;

    IWindow IWindowInput.Window => Window;

    public IReadOnlyList<SilkKeyboard> Keyboards { get; }

    IReadOnlyList<IKeyboard> IWindowInput.Keyboards => keyboards;

    public IReadOnlyList<SilkMouse> Mice { get; }

    IReadOnlyList<IMouse> IWindowInput.Mice => mice;

    public IReadOnlyList<ITouchscreen> Touchscreens => touchscreens;

    public SilkTextInputContext TextInput { get; }

    ITextInputContext IWindowInput.TextInput => TextInput;

    internal IReadOnlyList<SilkGamepad> Gamepads => [.. gamepads.Values];

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        native.ConnectionChanged -= OnConnectionChanged;
        TextInput.Dispose();
        foreach (SilkKeyboard keyboard in Keyboards)
        {
            keyboard.Dispose();
        }

        foreach (SilkMouse mouse in Mice)
        {
            mouse.Dispose();
        }

        foreach (SilkGamepad gamepad in gamepads.Values)
        {
            gamepad.Dispose();
        }

        gamepads.Clear();
        native.Dispose();
    }

    private void OnConnectionChanged(
        Silk.NET.Input.IInputDevice device,
        bool connected)
    {
        if (device is not Silk.NET.Input.IGamepad nativeGamepad)
        {
            return;
        }

        if (connected)
        {
            var gamepad = new SilkGamepad(nativeGamepad);
            gamepads[nativeGamepad] = gamepad;
            GamepadConnectionChanged?.Invoke(this, new(gamepad, true));
        }
        else if (gamepads.Remove(nativeGamepad, out SilkGamepad? gamepad))
        {
            GamepadConnectionChanged?.Invoke(this, new(gamepad, false));
            gamepad.Dispose();
        }
    }
}
