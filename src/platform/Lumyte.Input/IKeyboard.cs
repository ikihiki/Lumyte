namespace Lumyte.Input;

public interface IKeyboard
{
    event EventHandler<KeyChangedEventArgs>? KeyChanged;

    bool IsKeyPressed(Key key);
}
