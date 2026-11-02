using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<UiSharedElementKey, UiElement> _sharedElements = [];
    private readonly HashSet<UiPresenceEntry> _presenceEntries = [];
    private UiPresenceEntry[] _orderedPresence = [];
    private bool _presenceOrderDirty;
    private bool _hostExiting;
    private bool _logicalVisualCapture;
    private IReadOnlySet<UiElement>? _visualExclusions;

    internal IUiVisualHost? VisualHost { get; set; }

    internal bool PageTransitioning { get; private set; }

    internal ulong VisualRevision { get; private set; }

    internal IReadOnlyCollection<UiPresenceEntry> VisualEntries => _presenceEntries;

    internal bool IsVisualAlive => !_isDisposed;

    internal bool HostExiting => _hostExiting;

    internal UiElement? FindSharedElement(UiSharedElementKey key)
    {
        ThrowIfDisposed();
        return _sharedElements.GetValueOrDefault(key);
    }

    internal void AddPresence(UiPresenceEntry entry)
    {
        _presenceEntries.Add(entry);
        _presenceOrderDirty = true;
    }

    internal void RemovePresence(UiPresenceEntry entry)
    {
        entry.Detach();
        _presenceEntries.Remove(entry);
        _presenceOrderDirty = true;
        if (_presenceEntries.Count == 0)
        {
            _orderedPresence = [];
        }
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

        foreach ((Lumyte.Input.Abstractions.Key key, UiElement target) in _keyCaptures.ToArray())
        {
            if (target.IsTransitioning)
            {
                CancelKey(key);
            }
        }

        if (_focusedElement?.IsTransitioning == true)
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

            if (!_rootElement.IsLayoutDirty)
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

    internal DrawList2D CaptureVisual(UiElement widget, Vector2 scale, IReadOnlySet<UiElement>? exclusions = null, bool logical = false, bool includeAncestors = true, bool bounded = true)
    {
        ThrowIfDisposed();
        UiSignalRevision revision = SignalRevision;
        ulong visual = VisualRevision;
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(scale);
        Vector2 origin = VisualOrigin(widget);
        Rect2D clip = new(0, 0, _viewport.X, _viewport.Y);
        IReadOnlySet<UiElement>? previousExclusions = _visualExclusions;
        bool previousLogical = _logicalVisualCapture;
        for (UiElement? ancestor = widget.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (includeAncestors && ancestor.Widget.ClipToBounds)
            {
                Vector2 point = VisualOrigin(ancestor);
                clip = IntersectVisual(clip, new(point.X, point.Y, ancestor.Size.X, ancestor.Size.Y));
            }
        }

        if (includeAncestors)
        {
            canvas.ClipRect(new(clip.X - origin.X, clip.Y - origin.Y, clip.Width, clip.Height));
        }

        if (bounded || widget.Widget.ClipToBounds)
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

    internal void RecordNormalVisualPage(DrawingCanvas2D canvas, IReadOnlySet<UiElement> exclusions)
    {
        _visualExclusions = exclusions;
        try
        {
            if (!exclusions.Contains(_rootElement))
            {
                _rootElement.InvokeRecord(canvas);
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

    private static int PresenceDepth(UiPresenceEntry entry)
    {
        int depth = 0;
        for (UiElement? parent = entry.Widget.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static Vector2 VisualOrigin(UiElement widget)
    {
        Vector2 point = Vector2.Zero;
        for (UiElement? node = widget; node is not null; node = node.Parent)
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

        if (_presenceOrderDirty)
        {
            _orderedPresence = _presenceEntries.OrderBy(PresenceDepth).ToArray();
            _presenceOrderDirty = false;
        }

        foreach (UiPresenceEntry entry in _orderedPresence)
        {
            if (_isDisposed)
            {
                break;
            }

            UiElement widget = entry.Widget;

            bool hiddenParent = false;
            bool enteringParent = false;
            for (UiElement? parent = widget.Parent; parent is not null; parent = parent.Parent)
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
