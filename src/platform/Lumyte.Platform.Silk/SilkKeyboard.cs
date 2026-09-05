using Lumyte.Input;
using NativeKeyboard = Silk.NET.Input.IKeyboard;
using NativeKey = Silk.NET.Input.Key;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkKeyboard : IKeyboard, IDisposable
{
    private readonly NativeKeyboard native;
    private readonly HashSet<NativeKey> pressed = [];

    internal SilkKeyboard(NativeKeyboard native)
    {
        this.native = native;
        native.KeyDown += OnKeyDown;
        native.KeyUp += OnKeyUp;
    }

    public event EventHandler<KeyChangedEventArgs>? KeyChanged;

    public NativeKeyboard Native => native;

    public bool IsKeyPressed(Key key) => native.SupportedKeys
        .Any(nativeKey => SilkInputConversions.ToLumyte(nativeKey) == key
            && native.IsKeyPressed(nativeKey));

    public void Dispose()
    {
        native.KeyDown -= OnKeyDown;
        native.KeyUp -= OnKeyUp;
    }

    private void OnKeyDown(NativeKeyboard _, NativeKey key, int scanCode)
    {
        bool isRepeat = !pressed.Add(key);
        KeyChanged?.Invoke(
            this,
            new(SilkInputConversions.ToLumyte(key), true, isRepeat));
    }

    private void OnKeyUp(NativeKeyboard _, NativeKey key, int scanCode)
    {
        pressed.Remove(key);
        KeyChanged?.Invoke(this, new(SilkInputConversions.ToLumyte(key), false, false));
    }
}
