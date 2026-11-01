namespace Lumyte.UI;

/// <summary>Records child-slot retention without modifying a declaration or live element.</summary>
public sealed class UiWidgetRebuildContext
{
    private readonly UiElement _owner;
    private readonly Dictionary<WidgetChildren, int> _retained = [];

    internal UiWidgetRebuildContext(UiElement owner)
    {
        _owner = owner;
    }

    internal IReadOnlyDictionary<WidgetChildren, int> Retained => _retained;

    /// <summary>Plans retention of the same logical runtime slot in a new declaration.</summary>
    /// <param name="previous">The old configuration's slot.</param>
    /// <param name="replacement">The new uninitialized slot at the same registration position.</param>
    public void RetainChildren(WidgetChildren previous, WidgetChildren replacement)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!ReferenceEquals(previous.Owner, _owner.Widget) || previous.SlotIndex != replacement.SlotIndex
            || replacement.IsInitialized || !_retained.TryAdd(replacement, previous.SlotIndex))
        {
            throw new ArgumentException("Retention requires the owner's same logical, uninitialized slot.");
        }
    }
}
