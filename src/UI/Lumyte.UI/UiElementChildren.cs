using System.Collections;

namespace Lumyte.UI;

/// <summary>A stable, read-only runtime child slot owned by one persistent element.</summary>
public sealed class UiElementChildren : IReadOnlyList<UiElement>
{
    private IReadOnlyList<UiElement> _items = Array.Empty<UiElement>();

    internal UiElementChildren(UiElement owner, int slotIndex)
    {
        Owner = owner;
        SlotIndex = slotIndex;
    }

    /// <summary>Gets the number of currently adopted child instances.</summary>
    public int Count => _items.Count;

    internal UiElement Owner { get; }

    internal int SlotIndex { get; }

    /// <summary>Gets the mounted child at the specified position.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <returns>The persistent child instance.</returns>
    public UiElement this[int index] => _items[index];

    /// <summary>Enumerates the current child instances.</summary>
    /// <returns>The ordered enumerator.</returns>
    public IEnumerator<UiElement> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal bool Publish(IReadOnlyList<UiElement> items)
    {
        if (ReferenceEquals(items, this))
        {
            return false;
        }

        bool changed = items.Count != _items.Count;
        for (int index = 0; !changed && index < items.Count; index++)
        {
            changed |= !ReferenceEquals(items[index], _items[index]);
        }

        if (changed)
        {
            _items = items;
        }

        return changed;
    }
}
