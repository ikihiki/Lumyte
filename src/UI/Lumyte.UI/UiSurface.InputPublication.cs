namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private void CancelChangedInput(RebuildPlan plan)
    {
        if (_pointerCaptures.Count == 0)
        {
            return;
        }

        var cancelled = new HashSet<UiElement>(plan.Removed, ReferenceEqualityComparer.Instance);
        cancelled.UnionWith(plan.Updates.Where(update => update.CancelInput).Select(update => update.Element));
        List<Exception>? failures = null;
        foreach ((long pointerId, PointerCapture capture) in _pointerCaptures.ToArray())
        {
            bool eligible = true;
            for (UiElement? element = capture.Target; element is not null; element = element.Parent)
            {
                eligible &= UiInputGeometry.AcceptsInput(element);
            }

            if (cancelled.Contains(capture.Target) || !eligible)
            {
                try
                {
                    CancelPointer(pointerId, capture.Target);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
        }

        ThrowFailures(failures);
    }
}
