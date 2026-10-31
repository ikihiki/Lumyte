using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiPresenceEntry> _presenceEntries = [];
    private bool _hostExiting;
    private bool _logicalVisualCapture;
    private IReadOnlySet<Widget>? _visualExclusions;

    internal IUiVisualHost? VisualHost { get; set; }

    internal bool PageTransitioning { get; private set; }

    internal ulong VisualRevision { get; private set; }

    internal IReadOnlyCollection<UiPresenceEntry> VisualEntries => _presenceEntries;

    internal bool IsVisualAlive => !_isDisposed;

    internal bool HostExiting => _hostExiting;

    internal void AddPresence(UiPresenceEntry entry) => _presenceEntries.Add(entry);

    internal void RemovePresence(UiPresenceEntry entry)
    {
        entry.Detach();
        _presenceEntries.Remove(entry);
    }

    internal void InvalidateVisualRevision() => VisualRevision = checked(VisualRevision + 1);

    internal void BeginVisualExit()
    {
        _hostExiting = true;
        ReconcileVisuals();
    }

    internal void AdvanceVisualClock(TimePoint now) => AdvanceVisuals(now);

    internal void FreezeValueAnimations()
    {
        _animationUpdatesEnabled = false;
        foreach (UiAnimationProducer producer in _animationProducers)
        {
            producer.Freeze();
        }
    }

    internal void SetPageTransitioning(bool value)
    {
        if (PageTransitioning != value)
        {
            PageTransitioning = value;
            PresenceGateChanged();
        }
    }

    internal void PresenceGateChanged()
    {
        InvalidateInput();
        foreach ((long pointer, PointerCapture capture) in _pointerCaptures.ToArray())
        {
            if (capture.Target.IsTransitioning)
            {
                CancelPointer(pointer);
            }
        }

        foreach ((Lumyte.Input.Abstractions.Key key, Widget target) in _keyCaptures.ToArray())
        {
            if (target.IsTransitioning)
            {
                CancelKey(key);
            }
        }

        if (_focusedWidget?.IsTransitioning == true)
        {
            ChangeFocus(null);
        }
    }

    internal (UiPresenceEntry Entry, ulong Generation)[] CaptureVisualPresentation() => _presenceEntries.Where(entry => entry.AwaitingPresentation).Select(entry => (entry, entry.Generation)).ToArray();

    internal void ConfirmVisualPresentation((UiPresenceEntry Entry, ulong Generation)[] entries)
    {
        if (!_isDisposed)
        {
            foreach ((UiPresenceEntry entry, ulong generation) in entries)
            {
                entry.Presented(generation);
            }

            if (!_root.IsLayoutDirty)
            {
                UpdateInput();
            }
        }
    }

    internal void InterruptVisuals()
    {
        foreach (UiPresenceEntry entry in _presenceEntries.ToArray())
        {
            if (entry.IsActive)
            {
                entry.Skip();
            }
        }
    }

    internal DrawList2D CaptureVisual(Widget widget, Vector2 scale, IReadOnlySet<Widget>? exclusions = null, bool logical = false, bool includeAncestors = true, bool bounded = true)
    {
        ThrowIfDisposed();
        UiSignalRevision revision = SignalRevision;
        ulong visual = VisualRevision;
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(scale);
        Vector2 origin = VisualOrigin(widget);
        Rect2D clip = new(0, 0, _viewport.X, _viewport.Y);
        IReadOnlySet<Widget>? previousExclusions = _visualExclusions;
        bool previousLogical = _logicalVisualCapture;
        for (Widget? ancestor = widget.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (includeAncestors && ancestor.ClipToBounds)
            {
                Vector2 point = VisualOrigin(ancestor);
                clip = IntersectVisual(clip, new(point.X, point.Y, ancestor.Size.X, ancestor.Size.Y));
            }
        }

        if (includeAncestors)
        {
            canvas.ClipRect(new(clip.X - origin.X, clip.Y - origin.Y, clip.Width, clip.Height));
        }

        if (bounded || widget.ClipToBounds)
        {
            canvas.ClipRect(new(0, 0, widget.Size.X, widget.Size.Y));
        }

        _recordDepth++;
        _visualExclusions = exclusions ?? previousExclusions;
        _logicalVisualCapture = logical;
        try
        {
            if (!(_visualExclusions?.Contains(widget) ?? false))
            {
                widget.InvokeRecord(canvas);
            }
        }
        finally
        {
            _visualExclusions = previousExclusions;
            _logicalVisualCapture = previousLogical;
            _recordDepth--;
        }

        DrawList2D result = canvas.End();
        if (SignalRevision != revision || VisualRevision != visual)
        {
            throw new UiStateUpdateException(new InvalidOperationException("Visual content changed during capture."));
        }

        return result;
    }

    internal void RecordNormalVisualPage(DrawingCanvas2D canvas, IReadOnlySet<Widget> exclusions)
    {
        _visualExclusions = exclusions;
        try
        {
            if (!exclusions.Contains(_root))
            {
                _root.InvokeRecord(canvas);
            }
        }
        finally
        {
            _visualExclusions = null;
        }
    }

    internal void ConsumeInitialPresence()
    {
        foreach (UiPresenceEntry entry in _presenceEntries)
        {
            entry.Consume();
        }
    }

    internal void DeliverVisualNotifications()
    {
        foreach (UiPresenceEntry entry in _presenceEntries.ToArray())
        {
            if (_isDisposed)
            {
                break;
            }

            entry.Deliver();
        }
    }

    private static Vector2 VisualOrigin(Widget widget)
    {
        Vector2 point = Vector2.Zero;
        for (Widget? node = widget; node is not null; node = node.Parent)
        {
            point += new Vector2(node.Bounds.X, node.Bounds.Y);
        }

        return point;
    }

    private static Rect2D IntersectVisual(Rect2D first, Rect2D second)
    {
        float x = Math.Max(first.X, second.X);
        float y = Math.Max(first.Y, second.Y);
        return new(x, y, Math.Max(0, Math.Min(first.X + first.Width, second.X + second.Width) - x), Math.Max(0, Math.Min(first.Y + first.Height, second.Y + second.Height) - y));
    }

    private void ReconcileVisuals()
    {
        if (_presenceEntries.Count == 0)
        {
            return;
        }

        foreach (Widget widget in _root.CollectSubtree())
        {
            if (widget.Presence is not { } entry)
            {
                continue;
            }

            bool hiddenParent = false;
            bool enteringParent = false;
            for (Widget? parent = widget.Parent; parent is not null; parent = parent.Parent)
            {
                hiddenParent |= !parent.LayoutVisible;
                enteringParent |= parent.PresenceState == UiPresenceState.Entering;
            }

            if (hiddenParent)
            {
                entry.Suspend();
            }
            else if (enteringParent && !entry.Initialized)
            {
                entry.Consume();
            }
            else
            {
                entry.Reconcile();
            }
        }
    }

    private void AdvanceVisuals(TimePoint now)
    {
        DeliverVisualNotifications();
        foreach (UiPresenceEntry entry in _presenceEntries.ToArray())
        {
            entry.Advance(now);
        }

        ReconcileVisuals();
    }

    private void CompleteVirtualPresentation()
    {
        if (VisualHost is null && !_isDisposed)
        {
            ConfirmVisualPresentation(CaptureVisualPresentation());
            DeliverVisualNotifications();
        }
    }
}
