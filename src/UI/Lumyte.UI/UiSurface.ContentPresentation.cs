namespace Lumyte.UI;

/// <summary>Coordinates retained-content presentation with ordinary input ownership.</summary>
public sealed partial class UiSurface
{
    internal void ContentPresentationChanged(UiElement? fallback, long request)
    {
        InvalidateInput();
        List<Exception>? failures = null;
        foreach ((long id, PointerCapture capture) in _pointerCaptures.ToArray())
        {
            if (!AcceptsCurrentInput(capture.Target))
            {
                try
                {
                    CancelPointer(id, capture.Target);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
        }

        foreach ((Lumyte.Input.Abstractions.Key key, UiElement target) in _keyCaptures.ToArray())
        {
            if (!AcceptsCurrentInput(target))
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

        if (!_isDisposed && _focusedElement is { } focused && !AcceptsCurrentInput(focused))
        {
            try
            {
                ChangeFocus(null);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }

            if (!_isDisposed && fallback is not null && _focusRequest == request)
            {
                QueueItemFocus(fallback, request);
            }
        }

        ThrowInputFailures(failures);
    }
}
