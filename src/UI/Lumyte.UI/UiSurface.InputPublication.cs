namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private static bool AcceptsCurrentInput(UiElement target)
    {
        for (UiElement? element = target; element is not null; element = element.Parent)
        {
            if (!UiInputGeometry.AcceptsInput(element))
            {
                return false;
            }
        }

        return true;
    }

    private void CancelChangedInput(RebuildPlan plan)
    {
        if (_pointerCaptures.Count == 0 && _keyCaptures.Count == 0 && _focusedElement is null)
        {
            return;
        }

        var cancelled = new HashSet<UiElement>(plan.Removed, ReferenceEqualityComparer.Instance);
        cancelled.UnionWith(plan.Updates.Where(update => update.CancelInput).Select(update => update.Element));
        List<Exception>? failures = null;
        foreach ((long pointerId, PointerCapture capture) in _pointerCaptures.ToArray())
        {
            if (cancelled.Contains(capture.Target) || !AcceptsCurrentInput(capture.Target))
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

        foreach ((Lumyte.Input.Abstractions.Key key, UiElement target) in _keyCaptures.ToArray())
        {
            if (cancelled.Contains(target) || !AcceptsCurrentInput(target) || !target.Widget.Focusable)
            {
                try
                {
                    CancelKey(key, target);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
        }

        if (_focusedElement is { } focused && (cancelled.Contains(focused) || !AcceptsCurrentInput(focused) || !focused.Widget.Focusable))
        {
            try
            {
                ChangeFocus(null);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        ThrowFailures(failures);
    }
}
