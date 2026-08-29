using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsTouchscreen : ITouchscreen
{
    private readonly Dictionary<long, TouchPoint> activeTouches = [];

    public event EventHandler<TouchChangedEventArgs>? TouchChanged;

    public IReadOnlyList<TouchPoint> ActiveTouches => [.. activeTouches.Values];

    public bool TryGetTouch(long id, out TouchPoint touch) => activeTouches.TryGetValue(id, out touch);

    internal void ChangeTouch(long id, Vector2 position, TouchPhase phase, float? pressure)
    {
        Vector2 delta = activeTouches.TryGetValue(id, out TouchPoint previous)
            ? position - previous.Position
            : Vector2.Zero;
        var touch = new TouchPoint(id, position, delta, phase, pressure);

        if (phase is TouchPhase.Ended or TouchPhase.Cancelled)
        {
            activeTouches.Remove(id);
        }
        else
        {
            activeTouches[id] = touch;
        }

        TouchChanged?.Invoke(this, new(touch));
    }

    internal void Cancel(long id)
    {
        if (activeTouches.TryGetValue(id, out TouchPoint touch))
        {
            ChangeTouch(id, touch.Position, TouchPhase.Cancelled, touch.Pressure);
        }
    }

    internal void CancelAll()
    {
        foreach (long id in activeTouches.Keys.ToArray())
        {
            Cancel(id);
        }
    }
}
