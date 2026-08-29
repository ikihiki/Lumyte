using Lumyte.Input;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualKeyboard : IKeyboard
{
    private readonly HashSet<Key> pressedKeys = [];

    public event EventHandler<KeyChangedEventArgs>? KeyChanged;

    public bool IsKeyPressed(Key key) => pressedKeys.Contains(key);

    public void Press(Key key)
    {
        bool isRepeat = !pressedKeys.Add(key);
        KeyChanged?.Invoke(this, new(key, true, isRepeat));
    }

    public void Release(Key key)
    {
        if (pressedKeys.Remove(key))
        {
            KeyChanged?.Invoke(this, new(key, false, false));
        }
    }
}
