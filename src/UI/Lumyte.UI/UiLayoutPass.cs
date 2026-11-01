using System.Numerics;

namespace Lumyte.UI;

internal sealed class UiLayoutPass
{
    private static long _nextId;
    private readonly long _id = Interlocked.Increment(ref _nextId);
    private UiElement? _current;
    private Exception? _failure;

    internal void Compute(UiElement root, UiBoxConstraints constraints)
    {
        try
        {
            LayoutNode(root, constraints);
            root.LayoutOffsetSet = true;
            if (_failure is not null)
            {
                throw new InvalidOperationException("A failed layout operation was handled inside an override.", _failure);
            }
        }
        catch
        {
            foreach (UiElement element in UiSurface.CollectElements(root))
            {
                element.InvalidateLayoutCache();
            }

            throw;
        }
    }

    internal Vector2 LayoutChild(UiElement parent, UiElement child, UiBoxConstraints constraints)
    {
        try
        {
            VerifyChild(parent, child);
            return LayoutNode(child, constraints);
        }
        catch (Exception exception)
        {
            _failure ??= exception;
            throw;
        }
    }

    internal void SetChildOffset(UiElement parent, UiElement child, Vector2 offset)
    {
        try
        {
            VerifyChild(parent, child);
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (child.LayoutPassId != _id || !child.LayoutComplete || child.LayoutOffsetSet)
            {
                throw new InvalidOperationException("Assign a child's offset once, after laying it out.");
            }

            child.PublishLayout(child.Size, child.Widget.IsVisible ? offset : Vector2.Zero);
            child.LayoutOffsetSet = true;
        }
        catch (Exception exception)
        {
            _failure ??= exception;
            throw;
        }
    }

    private Vector2 LayoutNode(UiElement node, UiBoxConstraints constraints)
    {
        if (node.LayoutPassId == _id)
        {
            throw new InvalidOperationException("Each child can be laid out only once per pass.");
        }

        if (!node.Widget.IsVisible)
        {
            StageHidden(node);
            return Vector2.Zero;
        }

        BeginNode(node);
        UiBoxConstraints effective = constraints.Tighten(node.Widget.Width, node.Widget.Height);
        if (node.CanReuseLayout(effective))
        {
            node.PublishLayout(node.Size, Vector2.Zero);
            node.LayoutComplete = true;
            return node.Size;
        }

        UiElement? previous = _current;
        _current = node;
        try
        {
            Vector2 size = node.InvokeLayout(effective);
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y)
                || size.X < effective.MinWidth || size.X > effective.MaxWidth
                || size.Y < effective.MinHeight || size.Y > effective.MaxHeight)
            {
                throw new InvalidOperationException("Layout must return a finite size within its constraints.");
            }

            foreach (UiElementChildren collection in node.Slots)
            {
                ValidateChildren(collection);
            }

            node.PublishLayout(size, Vector2.Zero);
            node.CacheLayout(effective);
            node.LayoutComplete = true;
            return size;
        }
        catch (Exception exception)
        {
            _failure ??= exception;
            throw;
        }
        finally
        {
            _current = previous;
        }
    }

    private void StageHidden(UiElement root)
    {
        foreach (UiElement node in UiSurface.CollectElements(root))
        {
            BeginNode(node);
            node.InvalidateLayoutCache();
            node.LayoutComplete = true;
            node.LayoutOffsetSet = !ReferenceEquals(node, root);
            node.PublishLayout(Vector2.Zero, Vector2.Zero);
        }
    }

    private void ValidateChildren(UiElementChildren collection)
    {
        foreach (UiElement child in collection)
        {
            if (!child.Widget.IsVisible && child.LayoutPassId != _id)
            {
                StageHidden(child);
            }

            if (child.LayoutPassId != _id || !child.LayoutComplete
                || (child.Widget.IsVisible && !child.LayoutOffsetSet))
            {
                throw new InvalidOperationException("Every visible child must be laid out and positioned.");
            }
        }
    }

    private void VerifyChild(UiElement parent, UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(_current, parent) || !ReferenceEquals(child.Parent, parent))
        {
            throw new InvalidOperationException("Layout helpers can only operate on the current widget's direct children.");
        }
    }

    private void BeginNode(UiElement node)
    {
        node.LayoutPassId = _id;
        node.LayoutComplete = false;
        node.LayoutOffsetSet = false;
    }
}
