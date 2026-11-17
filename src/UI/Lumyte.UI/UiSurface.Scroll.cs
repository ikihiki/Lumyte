using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Provides viewport input arbitration.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<long, PanCandidate> _panCandidates = [];
    private UiElement? _revealFocus;
    private UiElement? _restoreItemFocus;
    private long _restoreItemFocusRequest;

    internal long FocusRequest => _focusRequest;

    /// <summary>Reveals a finite element-local rectangle through all scroll ancestors.</summary>
    /// <param name="element">The mounted target.</param>
    /// <param name="rectangle">The finite nonnegative local rectangle.</param>
    /// <returns>Whether any offset changed.</returns>
    public bool BringIntoView(UiElement element, Rect2D rectangle)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(element);
        if (!ReferenceEquals(element.Surface, this))
        {
            throw new ArgumentException("The element must belong to this surface.", nameof(element));
        }

        if (!float.IsFinite(rectangle.X) || !float.IsFinite(rectangle.Y) || !float.IsFinite(rectangle.Width) || !float.IsFinite(rectangle.Height) || rectangle.Width < 0 || rectangle.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        }

        _revealFocus = null;
        UpdateLayout();
        bool changed = false;
        Vector2 point = new(rectangle.X, rectangle.Y);
        Vector2 size = new(rectangle.Width, rectangle.Height);
        for (UiElement? node = element; node?.Parent is { } parent; node = parent)
        {
            point = UiLayoutMath.Add(point, new Vector2(node.Bounds.X, node.Bounds.Y));
            if (parent is ScrollViewElement scroll)
            {
                bool vertical = scroll.Widget.Orientation == UiOrientation.Vertical;
                float start = vertical ? point.Y : point.X;
                float length = vertical ? size.Y : size.X;
                float viewport = scroll.Controller.Viewport;
                float delta = start < 0 ? start : (double)start + length > viewport ? (float)Math.Min(start, (double)start + length - viewport) : 0;
                ulong revision = TreeRevision;
                float applied = delta - scroll.ApplyDelta(delta);
                if (_isDisposed || TreeRevision != revision || !ReferenceEquals(element.Surface, this)
                    || !ReferenceEquals(node.Parent, parent) || !ReferenceEquals(parent.Surface, this))
                {
                    return true;
                }

                changed |= applied != 0;
                point -= vertical ? new Vector2(0, applied) : new Vector2(applied, 0);
            }

            if (parent.Widget.ClipToBounds)
            {
                // Coordinates here are relative to the parent's body, before modifier padding.
                float left = -parent.ContentBounds.X;
                float top = -parent.ContentBounds.Y;
                float right = Math.Min(left + parent.Size.X, point.X + size.X);
                float bottom = Math.Min(top + parent.Size.Y, point.Y + size.Y);
                point = new(Math.Max(left, point.X), Math.Max(top, point.Y));
                if (right <= point.X || bottom <= point.Y)
                {
                    break;
                }

                size = new(right - point.X, bottom - point.Y);
            }

            point = UiLayoutMath.Add(point, parent.ContentOffset);
        }

        if (changed)
        {
            UpdateLayout();
        }

        return changed;
    }

    internal void QueueItemFocus(UiElement target, long request)
    {
        // Rebuild invalidation is not a request. Preserve only the intent captured in preparation.
        if (_focusRequest == request && ReferenceEquals(target.Surface, this) && AcceptsCurrentInput(target) && target.PermitsFocus)
        {
            _restoreItemFocus = target;
            _restoreItemFocusRequest = request;
        }
    }

    private void RevealPendingFocus()
    {
        UiElement? restore = _restoreItemFocus;
        _restoreItemFocus = null;
        if (restore is not null && !_isDisposed && _focusRequest == _restoreItemFocusRequest && ReferenceEquals(restore.Surface, this))
        {
            TryFocus(restore);
        }

        UiElement? target = _revealFocus;
        _revealFocus = null;
        if (target is not null && !_isDisposed && ReferenceEquals(target.Surface, this)
            && ReferenceEquals(target, _focusedElement))
        {
            for (UiElement? parent = target.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is ScrollViewElement)
                {
                    Rect2D region = target.SemanticActionRegion;
                    BringIntoView(target, new(target.ContentBounds.X + region.X, target.ContentBounds.Y + region.Y, region.Width, region.Height));
                    break;
                }
            }
        }
    }

    private bool RoutePan(UiPointerInput pointer, out bool consumed)
    {
        consumed = false;
        if (pointer.Phase == UiPointerPhase.Cancel)
        {
            _panCandidates.Remove(pointer.PointerId);
            return false;
        }

        if (pointer.Device != UiPointerDevice.Touch)
        {
            return false;
        }

        if (pointer.Phase == UiPointerPhase.Down)
        {
            UiElement? hit = HitTest(pointer.Position);

            // Editing and explicit pointer controls keep their own selection/drag gestures.
            for (UiElement? node = hit; node is not null; node = node.Parent)
            {
                if (node.BlocksAncestorPan)
                {
                    return false;
                }
            }

            var scrolls = new List<ScrollViewElement>();
            for (UiElement? node = hit; node is not null; node = node.Parent)
            {
                if (node is ScrollViewElement scroll && AcceptsCurrentInput(scroll))
                {
                    scrolls.Add(scroll);
                }
            }

            if (scrolls.Count != 0)
            {
                _panCandidates[pointer.PointerId] = new(pointer.Position, scrolls.ToArray());
            }

            return false;
        }

        if (!_panCandidates.TryGetValue(pointer.PointerId, out PanCandidate? candidate))
        {
            return false;
        }

        if (pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel)
        {
            _panCandidates.Remove(pointer.PointerId);
            return false;
        }

        if (candidate.Active && (!_pointerCaptures.TryGetValue(pointer.PointerId, out PointerCapture? panCapture)
            || !ReferenceEquals(panCapture.Target.Surface, this) || !AcceptsCurrentInput(panCapture.Target)))
        {
            _panCandidates.Remove(pointer.PointerId);
            return false;
        }

        Vector2 delta = new(
            (float)Math.Clamp((double)candidate.Last.X - pointer.Position.X, -float.MaxValue, float.MaxValue),
            (float)Math.Clamp((double)candidate.Last.Y - pointer.Position.Y, -float.MaxValue, float.MaxValue));
        if (!candidate.Active)
        {
            if (delta.LengthSquared() < 36)
            {
                return false;
            }

            bool vertical = Math.Abs(delta.Y) >= Math.Abs(delta.X);
            ScrollViewElement? owner = candidate.Scrolls.FirstOrDefault(scroll => (scroll.Widget.Orientation == UiOrientation.Vertical) == vertical && ReferenceEquals(scroll.Surface, this) && AcceptsCurrentInput(scroll));
            if (owner is null)
            {
                return false;
            }

            CancelPointer(pointer.PointerId);
            if (_isDisposed || !CapturePointer(owner, pointer))
            {
                _panCandidates.Remove(pointer.PointerId);
                return true;
            }

            candidate.Active = true;
        }

        candidate.Last = pointer.Position;
        foreach (ScrollViewElement scroll in candidate.Scrolls)
        {
            if (!ReferenceEquals(scroll.Surface, this) || !AcceptsCurrentInput(scroll))
            {
                continue;
            }

            bool vertical = scroll.Widget.Orientation == UiOrientation.Vertical;
            float residual = scroll.ApplyDelta(vertical ? delta.Y : delta.X);
            if (_isDisposed)
            {
                consumed = true;
                return true;
            }

            delta = vertical ? new(delta.X, residual) : new(residual, delta.Y);
        }

        consumed = true;
        return true;
    }

    private sealed class PanCandidate(Vector2 position, ScrollViewElement[] scrolls)
    {
        internal Vector2 Last { get; set; } = position;

        internal ScrollViewElement[] Scrolls { get; } = scrolls;

        internal bool Active { get; set; }
    }
}
