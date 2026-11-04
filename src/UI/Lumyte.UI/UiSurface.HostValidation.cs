namespace Lumyte.UI;

/// <summary>Indexes components whose configuration requires host service validation.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiElement> _hostValidationElements = [];

    internal ulong HostValidationRevision { get; private set; }

    internal IReadOnlyCollection<UiElement> HostValidationElements => _hostValidationElements;

    internal void ForgetHostValidation(UiElement element)
    {
        if (_hostValidationElements.Remove(element))
        {
            HostValidationRevision = checked(HostValidationRevision + 1);
        }
    }

    private void UpdateHostValidation(RebuildPlan plan)
    {
        bool changed = false;
        foreach (UiElement removed in plan.Removed)
        {
            changed |= _hostValidationElements.Remove(removed);
        }

        foreach (ElementUpdate update in plan.Updates)
        {
            if (_isDisposed)
            {
                return;
            }

            bool previous = _hostValidationElements.Contains(update.Element);
            bool current = update.Element.NeedsHostValidation;
            if (_isDisposed)
            {
                return;
            }

            if (current)
            {
                _hostValidationElements.Add(update.Element);
            }
            else
            {
                _hostValidationElements.Remove(update.Element);
            }

            changed |= previous != current || (current && (update.Previous is null || update.Element.HostRequirementsChanged(update.Previous, update.Next)));
        }

        if (changed)
        {
            HostValidationRevision = checked(HostValidationRevision + 1);
        }
    }
}
