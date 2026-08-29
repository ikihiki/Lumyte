using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class MouseGestureSource : ITouchscreen, IDisposable
{
    private const long PointerId = 0;
    private readonly MouseButton button;
    private readonly IMouse mouse;
    private TouchPoint? activeTouch;
    private bool disposed;

    public MouseGestureSource(IMouse mouse, MouseButton button = MouseButton.Left)
    {
        this.mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        this.button = button;
        mouse.ButtonChanged += OnButtonChanged;
        mouse.Moved += OnMoved;
    }

    public event EventHandler<TouchChangedEventArgs>? TouchChanged;

    public IReadOnlyList<TouchPoint> ActiveTouches => activeTouch is TouchPoint touch ? [touch] : [];

    public bool TryGetTouch(long id, out TouchPoint touch)
    {
        if (id == PointerId && activeTouch is TouchPoint active)
        {
            touch = active;
            return true;
        }

        touch = default;
        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mouse.ButtonChanged -= OnButtonChanged;
        mouse.Moved -= OnMoved;
    }

    private void OnButtonChanged(object? sender, MouseButtonChangedEventArgs eventArgs)
    {
        if (eventArgs.Button != button)
        {
            return;
        }

        TouchPhase phase = eventArgs.IsPressed ? TouchPhase.Began : TouchPhase.Ended;
        var touch = new TouchPoint(PointerId, eventArgs.Position, default, phase, null);
        activeTouch = eventArgs.IsPressed ? touch : null;
        TouchChanged?.Invoke(this, new(touch));
    }

    private void OnMoved(object? sender, MouseMovedEventArgs eventArgs)
    {
        if (activeTouch is null)
        {
            return;
        }

        var touch = new TouchPoint(
            PointerId,
            eventArgs.Position,
            eventArgs.Delta,
            TouchPhase.Moved,
            null);
        activeTouch = touch;
        TouchChanged?.Invoke(this, new(touch));
    }
}
