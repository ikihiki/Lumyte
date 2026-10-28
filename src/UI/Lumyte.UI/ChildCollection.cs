using System.Collections.ObjectModel;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class ChildCollection(Widget owner, int maximumCount)
{
    private IReadOnlyList<Widget> _items = Array.Empty<Widget>();

    internal IReadOnlyList<Widget> Items => _items;

    internal void Replace(IReadOnlyList<Widget> children)
    {
        using Widget.MutationScope mutation = owner.BeginMutation();
        ArgumentNullException.ThrowIfNull(children);
        int count = children.Count;
        if (count < 0 || count > maximumCount)
        {
            throw new ArgumentException("The number of children is invalid for this container.", nameof(children));
        }

        var replacement = new Widget[count];
        var included = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < count; index++)
        {
            Widget? child = children[index];
            if (child is null || !included.Add(child))
            {
                throw new ArgumentException("Children must be non-null and distinct by reference.", nameof(children));
            }

            replacement[index] = child;
        }

        foreach (Widget child in replacement)
        {
            for (Widget? ancestor = owner; ancestor is not null; ancestor = ancestor.Owner)
            {
                if (ReferenceEquals(child, ancestor))
                {
                    throw new ArgumentException("A child cannot be its own ancestor.", nameof(children));
                }
            }

            if (child.IsMutating)
            {
                throw new InvalidOperationException("A child subtree mutation is already in progress.");
            }

            if (child.ParentCollection is not null && !ReferenceEquals(child.ParentCollection, this))
            {
                throw new ArgumentException("The child already belongs to another parent or slot.", nameof(children));
            }

            if (child.Surface is not null && !ReferenceEquals(child.ParentCollection, this))
            {
                throw new ArgumentException("The child is already connected to a surface.", nameof(children));
            }
        }

        // Complete every allocation and traversal before changing any ownership.
        ReadOnlyCollection<Widget> view = Array.AsReadOnly(replacement);
        var removed = new List<Widget>();
        var detached = new List<Widget>();
        var added = new List<Widget>();
        var attached = new List<Widget>();
        foreach (Widget child in _items)
        {
            if (!included.Contains(child))
            {
                removed.Add(child);
                if (owner.Surface is not null)
                {
                    detached.AddRange(child.CollectSubtree());
                }
            }
        }

        foreach (Widget child in replacement)
        {
            if (!ReferenceEquals(child.ParentCollection, this))
            {
                added.Add(child);
                if (owner.Surface is not null)
                {
                    attached.AddRange(child.CollectSubtree());
                }
            }
        }

        foreach (Widget node in detached)
        {
            node.SetSurface(null);
        }

        foreach (Widget child in removed)
        {
            child.SetParent(null, null);
        }

        foreach (Widget child in added)
        {
            child.SetParent(owner, this);
        }

        foreach (Widget node in attached)
        {
            node.SetSurface(owner.Surface);
        }

        _items = view;
    }
}
