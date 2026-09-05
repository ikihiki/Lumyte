using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualTouchscreen : ITouchscreen
{
    private readonly Dictionary<long, TouchPoint> touches = [];

    public event EventHandler<TouchChangedEventArgs>? TouchChanged;

    public IReadOnlyList<TouchPoint> ActiveTouches => [.. touches.Values];

    public bool TryGetTouch(long id, out TouchPoint touch) => touches.TryGetValue(id, out touch);

    public void Begin(long id, Vector2 position) => Change(id, position, TouchPhase.Began);

    public void Move(long id, Vector2 position) => Change(id, position, TouchPhase.Moved);

    public void End(long id, Vector2 position) => Change(id, position, TouchPhase.Ended);

    private void Change(long id, Vector2 position, TouchPhase phase)
    {
        Vector2 previous = touches.TryGetValue(id, out TouchPoint current)
            ? current.Position
            : position;
        var touch = new TouchPoint(id, position, position - previous, phase, null);
        if (phase is TouchPhase.Ended or TouchPhase.Cancelled)
        {
            touches.Remove(id);
        }
        else
        {
            touches[id] = touch;
        }

        TouchChanged?.Invoke(this, new(touch));
    }
}
