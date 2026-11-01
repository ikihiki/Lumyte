using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Exposes a prepared child topology before any live fields are changed.</summary>
public sealed class UiElementUpdateContext
{
    private readonly Widget _next;
    private readonly IReadOnlyList<UiElement>[] _slots;

    internal UiElementUpdateContext(Widget next, IReadOnlyList<UiElement>[] slots)
    {
        _next = next;
        _slots = slots;
    }

    /// <summary>Gets the child instances planned for one slot of the next configuration.</summary>
    /// <param name="slot">A declaration slot owned by the next configuration.</param>
    /// <returns>The proposed children, including passive new instances.</returns>
    public IReadOnlyList<UiElement> GetChildren(WidgetChildren slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!ReferenceEquals(slot.Owner, _next))
        {
            throw new ArgumentException("The slot must belong to the proposed configuration.", nameof(slot));
        }

        return _slots[slot.SlotIndex];
    }
}
