namespace Lumyte.UI;

/// <summary>Connects a element tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    /// <summary>Requests focus for a mounted instance, or clears focus with null.</summary>
    /// <param name="element">The runtime target.</param>
    /// <returns>The operation result.</returns>
    public bool TryFocus(UiElement? element)
    {
        ThrowIfDisposed();
        if (element is not null && !ReferenceEquals(element.Surface, this))
        {
            throw new ArgumentException("The element does not belong to this surface.", nameof(element));
        }

        if (element is not null && !IsFocusEligible(element))
        {
            return false;
        }

        return ChangeFocus(element);
    }
}
