using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;

namespace Lumyte.Maps.UI;

/// <summary>Adapts a borrowed CPU map session to ordinary and retained UI recording.</summary>
public sealed class MapElement : UiElement<MapView>
{
    private MapSession? _attached;
    private ulong _revision;
    private long? _dragPointer;
    private Vector2 _lastPosition;

    internal MapElement(MapView widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool PreservePointerGesture => true;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiWheelInput wheel && context.PointerPosition is { } position && wheel.Delta.Y != 0)
        {
            Widget.Session.ZoomAt(position, -wheel.Delta.Y / 160.0);
            return UiInputResult.Handled;
        }

        if (context.Input is not UiPointerInput pointer)
        {
            return UiInputResult.Unhandled;
        }

        if (pointer.Phase == UiPointerPhase.Down && _dragPointer is null && context.IsInside && context.CapturePointer())
        {
            _dragPointer = pointer.PointerId;
            _lastPosition = pointer.Position;
            return UiInputResult.Handled;
        }

        if (_dragPointer == pointer.PointerId && context.IsCaptured)
        {
            if (pointer.Phase == UiPointerPhase.Move)
            {
                Widget.Session.Pan(pointer.Position - _lastPosition);
                _lastPosition = pointer.Position;
            }
            else
            {
                _dragPointer = null;
            }

            return UiInputResult.Handled;
        }

        return UiInputResult.Unhandled;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
    {
        if (!state.IsCaptured && _dragPointer == state.PointerId)
        {
            _dragPointer = null;
        }
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(MapView next, UiElementUpdateContext context)
    {
        ArgumentNullException.ThrowIfNull(next.Session);
        if (IsMounted && !ReferenceEquals(Widget.Session, next.Session))
        {
            throw new InvalidOperationException("Replace the map element identity before replacing its borrowed session.");
        }

        return null;
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(MapView previous, MapView next) =>
        ReferenceEquals(previous.Session, next.Session) ? UiInvalidation.None : UiInvalidation.All;

    /// <inheritdoc/>
    protected override void OnMounted()
    {
        MapSession session = Widget.Session;
        session.Attach(this);
        _attached = session;
        _revision = session.DisplayRevision;
        session.Changed += OnChanged;
    }

    /// <inheritdoc/>
    protected override void OnUnmounted()
    {
        _dragPointer = null;
        if (_attached is { } session)
        {
            session.Changed -= OnChanged;
            session.Detach(this);
            _attached = null;
        }
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = constraints.Constrain(new(
            constraints.HasBoundedWidth ? constraints.MaxWidth : constraints.MinWidth,
            constraints.HasBoundedHeight ? constraints.MaxHeight : constraints.MinHeight));
        Widget.Session.SetViewport(size);
        return size;
    }

    /// <inheritdoc/>
    protected override void ValidateRetainedDrawing()
    {
        if (_revision != Widget.Session.DisplayRevision)
        {
            OnChanged();
        }
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        Widget.Session.Record(canvas);
        _revision = Widget.Session.DisplayRevision;
    }

    // Notifications occur on the UI owner; never reenter the session here.
    private void OnChanged() => InvalidateDrawing();
}
