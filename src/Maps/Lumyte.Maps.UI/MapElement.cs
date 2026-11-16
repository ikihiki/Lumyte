using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;

namespace Lumyte.Maps.UI;

/// <summary>Adapts a borrowed CPU map session to ordinary and retained UI recording.</summary>
public sealed class MapElement : UiElement<MapView>
{
    private MapSession? _attached;
    private ulong _revision;

    internal MapElement(MapView widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

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
