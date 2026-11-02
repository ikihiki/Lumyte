using System.Numerics;

namespace Lumyte.UI;

/// <summary>Defers reactive publication until input delivery finishes.</summary>
public sealed partial class UiSurface
{
    /// <summary>Routes logical input, returning whether this surface consumed it.</summary>
    /// <param name="input">The host-selected input.</param>
    /// <remarks>Call UpdateLayout before normal input delivery. Dispatch never updates geometry.</remarks>
    /// <returns>Whether a widget consumed the input or a captured series was delivered.</returns>
    public bool Dispatch(UiInput input)
    {
        _dispatchDepth++;
        try
        {
            return DispatchCore(input);
        }
        finally
        {
            _dispatchDepth--;
            ProcessSignals(false);
        }
    }

    private bool DispatchCore(UiInput input)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        if (input is UiFocusLostInput)
        {
            bool cancelled = CancelAllInput();
            return (!_isDisposed && RouteInput(_rootElement, input)) || cancelled;
        }

        if (input is UiKeyCancelInput keyCancel)
        {
            bool consumed = _consumedKeys.Contains(keyCancel.Key);
            return CancelKey(keyCancel.Key) || consumed;
        }

        if (input is UiKeyInput key)
        {
            return DispatchKey(key);
        }

        if (input is not UiPointerInput pointer)
        {
            return RouteInput(_focusedElement ?? _rootElement, input);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pointer.PointerId);
        if (!float.IsFinite(pointer.Position.X) || !float.IsFinite(pointer.Position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (pointer.Phase == UiPointerPhase.Cancel)
        {
            return CancelPointer(pointer.PointerId);
        }

        if (pointer.Phase is not (UiPointerPhase.Down or UiPointerPhase.Move or UiPointerPhase.Up))
        {
            return false;
        }

        if (_pointerCaptures.TryGetValue(pointer.PointerId, out PointerCapture? capture))
        {
            if (pointer.Phase == UiPointerPhase.Down)
            {
                return true;
            }

            capture.Position = pointer.Position;
            if (pointer.Phase == UiPointerPhase.Up)
            {
                ReleasePointer(pointer.PointerId);
            }

            if (!_isDisposed && ReferenceEquals(capture.Target.Surface, this))
            {
                DeliverInput(capture.Target, pointer, true);
            }

            return true;
        }

        if (!UiInputGeometry.Contains(Vector2.Zero, _viewport, pointer.Position))
        {
            if (pointer.Phase == UiPointerPhase.Down)
            {
                ChangeFocus(null);
            }

            return false;
        }

        UiElement? hit = _rootElement.FindInputHit(pointer.Position, _inputVersion);
        long focusChange = _focusChange;
        long focusRequest = _focusRequest;
        bool handled = hit is not null && RouteInput(hit, pointer);
        if (!_isDisposed && pointer.Phase == UiPointerPhase.Down && _focusChange == focusChange && _focusRequest == focusRequest
            && (!handled || _pointerCaptures.ContainsKey(pointer.PointerId)))
        {
            ChangeFocus(null);
        }

        return handled;
    }
}
