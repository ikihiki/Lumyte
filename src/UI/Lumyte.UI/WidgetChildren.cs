using System.Collections;
using System.Collections.ObjectModel;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A widget-owned ordered child collection that can be initialized successfully only once.</summary>
public sealed class WidgetChildren : IReadOnlyList<Widget>, ICompositionContent<Widget>
{
    private readonly Widget _owner;
    private readonly int _maximumCount;
    private ReadOnlyCollection<Widget> _items = Array.AsReadOnly(Array.Empty<Widget>());
    private bool _initialized;

    internal WidgetChildren(Widget owner, int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        _owner = owner;
        _maximumCount = maximumCount;
    }

    /// <summary>Gets the number of initialized children, or zero before initialization.</summary>
    public int Count => _items.Count;

    internal IReadOnlyList<Widget> Items => _items;

    /// <summary>Gets a child by its zero-based position.</summary>
    /// <param name="index">The child position.</param>
    /// <returns>The owned child.</returns>
    public Widget this[int index] => _items[index];

    /// <summary>Copies and adopts children once; even an empty successful initialization consumes the one assignment.</summary>
    /// <param name="content">An array of non-null, distinct, unowned children; arbitrary list implementations are not accepted.</param>
    public void Initialize(params Widget[] content)
    {
        _owner.VerifyMutationAllowed();
        if (_initialized)
        {
            throw new InvalidOperationException("Widget children can only be initialized once.");
        }

        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > _maximumCount)
        {
            throw new ArgumentException("The number of children exceeds this collection's capacity.", nameof(content));
        }

        var snapshot = (Widget[])content.Clone();
        var included = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        foreach (Widget child in snapshot)
        {
            if (child is null || !included.Add(child))
            {
                throw new ArgumentException("Children must be non-null and distinct by reference.", nameof(content));
            }

            for (Widget? ancestor = _owner; ancestor is not null; ancestor = ancestor.Owner)
            {
                if (ReferenceEquals(child, ancestor))
                {
                    throw new ArgumentException("A child cannot be its own ancestor.", nameof(content));
                }
            }

            if (child.Parent is not null || child.Surface is not null)
            {
                throw new ArgumentException("A child must have neither a parent nor a connected surface.", nameof(content));
            }
        }

        // Finish validation and allocations before publishing ownership, without invoking user code.
        ReadOnlyCollection<Widget> view = Array.AsReadOnly(snapshot);
        var attached = new List<Widget>();
        if (_owner.Surface is not null)
        {
            foreach (Widget child in snapshot)
            {
                attached.AddRange(child.CollectSubtree());
            }
        }

        foreach (Widget child in snapshot)
        {
            child.SetParent(_owner);
        }

        foreach (Widget child in attached)
        {
            child.SetSurface(_owner.Surface);
        }

        _items = view;
        _initialized = true;
        _owner.MarkLayoutDirty();
    }

    /// <summary>Returns an enumerator over the immutable child snapshot.</summary>
    /// <returns>The ordered enumerator.</returns>
    public IEnumerator<Widget> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
