using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsWindowInput : IWindowInput
{
    private readonly IKeyboard[] keyboards;
    private readonly IMouse[] mice;
    private readonly ITouchscreen[] touchscreens;

    internal WindowsWindowInput(WindowsWindow window)
    {
        Window = window;
        Keyboard = new();
        Mouse = new(window);
        Touchscreen = new();
        TextInput = new(window);
        keyboards = [Keyboard];
        mice = [Mouse];
        touchscreens = [Touchscreen];
    }

    public WindowsWindow Window { get; }

    IWindow IWindowInput.Window => Window;

    public WindowsKeyboard Keyboard { get; }

    public WindowsMouse Mouse { get; }

    public WindowsTouchscreen Touchscreen { get; }

    public WindowsTextInputContext TextInput { get; }

    ITextInputContext IWindowInput.TextInput => TextInput;

    public IReadOnlyList<IKeyboard> Keyboards => keyboards;

    public IReadOnlyList<IMouse> Mice => mice;

    public IReadOnlyList<ITouchscreen> Touchscreens => touchscreens;

    internal void Dispose()
    {
        Mouse.Dispose();
        TextInput.Dispose();
    }
}
