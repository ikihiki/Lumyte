using System.Collections;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A component-defined declaration slot initialized successfully at most once.</summary>
public sealed class WidgetChildren : IReadOnlyList<Widget>, ICompositionContent<Widget>
{
    private readonly int _maximumCount;
    private IReadOnlyList<Widget> _items = Array.Empty<Widget>();
    private bool _initialized;

    internal WidgetChildren(Widget owner, int slotIndex, int maximumCount, bool isPortal = false)
    {
        Owner = owner;
        SlotIndex = slotIndex;
        _maximumCount = maximumCount;
        IsPortal = isPortal;
    }

    /// <summary>Gets the number of declared children.</summary>
    public int Count => _items.Count;

    internal Widget Owner { get; }

    internal int SlotIndex { get; }

    internal bool IsPortal { get; }

    internal bool IsInitialized => _initialized;

    /// <summary>Gets the child declaration at the specified position.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <returns>The passive declaration.</returns>
    public Widget this[int index] => _items[index];

    /// <summary>Copies and adopts the supplied declarations exactly once, including an empty array.</summary>
    /// <param name="content">Distinct non-null passive declarations, which may be shared between different owners.</param>
    public void Initialize(params Widget[] content)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("Widget children can only be initialized once.");
        }

        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > _maximumCount)
        {
            throw new ArgumentException("The slot's capacity was exceeded.", nameof(content));
        }

        var snapshot = (Widget[])content.Clone();
        var included = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        foreach (Widget child in snapshot)
        {
            if (child is null || !included.Add(child))
            {
                throw new ArgumentException("Children must be non-null and distinct within a slot.", nameof(content));
            }

            foreach (WidgetChildren slot in Owner.ChildCollections)
            {
                for (int index = 0; index < slot.Count; index++)
                {
                    if (ReferenceEquals(slot[index], child))
                    {
                        throw new ArgumentException("A declaration may appear only once within one parent's slots.", nameof(content));
                    }
                }
            }

            if (ReferenceEquals(Owner, child) || (Owner.HasDeclarationParent && Contains(child, Owner)))
            {
                throw new ArgumentException("A declaration cannot contain itself.", nameof(content));
            }
        }

        IReadOnlyList<Widget> view = Array.AsReadOnly(snapshot);
        foreach (Widget child in snapshot)
        {
            child.HasDeclarationParent = true;
        }

        _items = view;
        _initialized = true;
    }

    /// <summary>Enumerates the unchanged construction-time declarations.</summary>
    /// <returns>The declaration enumerator.</returns>
    public IEnumerator<Widget> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool Contains(Widget root, Widget target)
    {
        var pending = new Stack<Widget>();
        var visited = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.TryPop(out Widget? node))
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }

            if (visited.Add(node))
            {
                foreach (WidgetChildren slot in node.ChildCollections)
                {
                    foreach (Widget child in slot)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return false;
    }
}
