using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsKeyboard : IKeyboard
{
    private readonly HashSet<Key> pressedKeys = [];

    public event EventHandler<KeyChangedEventArgs>? KeyChanged;

    public bool IsKeyPressed(Key key) => pressedKeys.Contains(key);

    internal void ChangeKey(Key key, bool isPressed, bool isRepeat)
    {
        if (isPressed)
        {
            pressedKeys.Add(key);
        }
        else
        {
            pressedKeys.Remove(key);
        }

        KeyChanged?.Invoke(this, new(key, isPressed, isRepeat));
    }
}
