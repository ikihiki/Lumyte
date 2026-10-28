using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class UiLayoutPass
{
    private readonly Dictionary<Widget, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private Widget? _current;
    private Exception? _failure;

    internal void Compute(Widget root, UiBoxConstraints constraints)
    {
        LayoutNode(root, constraints);
        _entries[root].OffsetSet = true;
        if (_failure is not null)
        {
            throw new InvalidOperationException("A failed layout operation was handled inside an override.", _failure);
        }
    }

    internal void Publish()
    {
        foreach ((Widget node, Entry entry) in _entries)
        {
            node.PublishLayout(entry.Size, entry.Offset);
        }
    }

    internal Vector2 LayoutChild(Widget parent, Widget child, UiBoxConstraints constraints)
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

    internal void SetChildOffset(Widget parent, Widget child, Vector2 offset)
    {
        try
        {
            VerifyChild(parent, child);
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (!_entries.TryGetValue(child, out Entry? entry) || !entry.Complete || entry.OffsetSet)
            {
                throw new InvalidOperationException("Assign a child's offset once, after laying it out.");
            }

            entry.Offset = child.IsVisible ? offset : Vector2.Zero;
            entry.OffsetSet = true;
        }
        catch (Exception exception)
        {
            _failure ??= exception;
            throw;
        }
    }

    private Vector2 LayoutNode(Widget node, UiBoxConstraints constraints)
    {
        if (_entries.ContainsKey(node))
        {
            throw new InvalidOperationException("Each child can be laid out only once per pass.");
        }

        if (!node.IsVisible)
        {
            StageHidden(node);
            return Vector2.Zero;
        }

        var entry = new Entry();
        _entries.Add(node, entry);
        Widget? previous = _current;
        _current = node;
        try
        {
            UiBoxConstraints effective = constraints.Tighten(node.Width, node.Height);
            Vector2 size = node.InvokeLayout(effective);
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y)
                || size.X < effective.MinWidth || size.X > effective.MaxWidth
                || size.Y < effective.MinHeight || size.Y > effective.MaxHeight)
            {
                throw new InvalidOperationException("Layout must return a finite size within its constraints.");
            }

            ValidateChildren(node.BackgroundCollection);
            ValidateChildren(node.ContentCollection);
            entry.Size = size;
            entry.Complete = true;
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

    private void StageHidden(Widget root)
    {
        foreach (Widget node in root.CollectSubtree())
        {
            _entries.Add(node, new Entry { Complete = true, OffsetSet = !ReferenceEquals(node, root) });
        }
    }

    private void ValidateChildren(WidgetChildren collection)
    {
        foreach (Widget child in collection.Items)
        {
            if (!child.IsVisible && !_entries.ContainsKey(child))
            {
                StageHidden(child);
            }

            if (!_entries.TryGetValue(child, out Entry? entry) || !entry.Complete
                || (child.IsVisible && !entry.OffsetSet))
            {
                throw new InvalidOperationException("Every visible child must be laid out and positioned.");
            }
        }
    }

    private void VerifyChild(Widget parent, Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(_current, parent) || !ReferenceEquals(child.Owner, parent))
        {
            throw new InvalidOperationException("Layout helpers can only operate on the current widget's direct children.");
        }
    }

    private sealed class Entry
    {
        internal Vector2 Size { get; set; }

        internal Vector2 Offset { get; set; }

        internal bool Complete { get; set; }

        internal bool OffsetSet { get; set; }
    }
}
