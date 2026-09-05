namespace Lumyte.Input;

public interface ITouchscreen
{
    event EventHandler<TouchChangedEventArgs>? TouchChanged;

    IReadOnlyList<TouchPoint> ActiveTouches { get; }

    bool TryGetTouch(long id, out TouchPoint touch);
}
