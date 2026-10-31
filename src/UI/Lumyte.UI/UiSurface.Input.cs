using System.Numerics;
using System.Runtime.ExceptionServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<long, PointerCapture> _pointerCaptures = new();
    private object? _inputVersion;
    private bool _inputDirty = true;

    internal object? InputVersionForWindowing => _inputVersion;

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
        }
    }

    internal void InvalidateInput() => _inputDirty = true;

    internal bool IsInside(Widget target, Vector2 position) =>
        ReferenceEquals(target.InputVersion, _inputVersion) && target.InputBounds.Contains(position);

    internal bool CapturePointer(Widget target, UiInput input)
    {
        ThrowIfDisposed();
        if (input is not UiPointerInput { Phase: UiPointerPhase.Down or UiPointerPhase.Move } pointer)
        {
            throw new InvalidOperationException("Pointer capture requires pointer Down or Move.");
        }

        if (!ReferenceEquals(target.Surface, this) || !ReferenceEquals(target.InputVersion, _inputVersion))
        {
            return false;
        }

        if (_pointerCaptures.TryGetValue(pointer.PointerId, out PointerCapture? existing))
        {
            return ReferenceEquals(existing.Target, target);
        }

        var capture = new PointerCapture(target, pointer.Position);
        _pointerCaptures.Add(pointer.PointerId, capture);
        NotifyCapture(pointer.PointerId, capture, true);
        return true;
    }

    internal bool CancelPointer(long pointerId, Widget? expected = null)
    {
        if (!_pointerCaptures.TryGetValue(pointerId, out PointerCapture? capture)
            || (expected is not null && !ReferenceEquals(capture.Target, expected)))
        {
            return false;
        }

        ReleasePointer(pointerId, true);
        return true;
    }

    private bool DispatchCore(UiInput input)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(input);
        if (input is UiFocusLostInput)
        {
            bool cancelled = CancelAllInput();
            return (!_isDisposed && RouteInput(_root, input)) || cancelled;
        }

        ThrowSignalFailure();
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
            return RouteInput(_focusedWidget ?? _root, input);
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

        Widget? hit = _root.FindInputHit(pointer.Position, _inputVersion);
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

    private bool RouteInput(Widget target, UiInput input) => RouteInputResult(target, input) == UiInputResult.Handled;

    private UiInputResult RouteInputResult(Widget target, UiInput input)
    {
        for (Widget? node = target; node is not null; node = node.Owner)
        {
            UiInputResult result = DeliverInput(node, input, false);
            if (input is UiPointerInput pointer && _pointerCaptures.ContainsKey(pointer.PointerId))
            {
                return UiInputResult.Handled;
            }

            if (input is UiKeyInput key && _keyCaptures.ContainsKey(key.Key))
            {
                return UiInputResult.Handled;
            }

            if (result != UiInputResult.Unhandled)
            {
                return result;
            }

            if (_isDisposed || !ReferenceEquals(node.Surface, this))
            {
                return UiInputResult.StopPropagation;
            }
        }

        return UiInputResult.Unhandled;
    }

    private UiInputResult DeliverInput(Widget target, UiInput input, bool isCaptured, bool isKeyCaptured = false)
    {
        var context = new UiInputContext(this, target, input, isCaptured, isKeyCaptured);
        long reset = _inputReset;
        try
        {
            return target.InvokeInput(in context);
        }
        catch (Exception failure)
        {
            if (input is UiPointerInput { Phase: UiPointerPhase.Down or UiPointerPhase.Move } pointer)
            {
                try
                {
                    CancelPointer(pointer.PointerId, target);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(failure, cleanupFailure);
                }
            }

            if (input is UiKeyInput { IsDown: true } key)
            {
                if (!_isDisposed && _inputReset == reset)
                {
                    _consumedKeys.Add(key.Key);
                }

                try
                {
                    CancelKey(key.Key, target);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(failure, cleanupFailure);
                }
            }

            throw;
        }
    }

    private void ReleasePointer(long pointerId, bool cancel = false)
    {
        if (_pointerCaptures.Remove(pointerId, out PointerCapture? capture))
        {
            NotifyReleased(pointerId, capture, cancel);
        }
    }

    private void NotifyCapture(long pointerId, PointerCapture capture, bool isCaptured) =>
        capture.Target.InvokePointerCaptureChanged(new(pointerId, isCaptured, capture.Position, isCaptured && IsInside(capture.Target, capture.Position)));

    private void NotifyReleased(long pointerId, PointerCapture capture, bool cancel)
    {
        Exception? notificationFailure = null;
        try
        {
            NotifyCapture(pointerId, capture, false);
        }
        catch (Exception failure)
        {
            notificationFailure = failure;
        }

        if (cancel)
        {
            try
            {
                DeliverInput(capture.Target, new UiPointerInput(pointerId, UiPointerPhase.Cancel, capture.Position), true);
            }
            catch (Exception failure)
            {
                if (notificationFailure is not null)
                {
                    throw new AggregateException(notificationFailure, failure);
                }

                throw;
            }
        }

        if (notificationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(notificationFailure).Throw();
        }
    }

    private void UpdateInput()
    {
        if (!_inputDirty)
        {
            return;
        }

        _inputVersion = new object();
        _root.UpdateInput(Vector2.Zero, UiInputBounds.From(Vector2.Zero, _viewport), _inputVersion);
        RebuildFocusCandidates();
        _inputDirty = false;
        RefreshFocusState();

        List<Exception>? failures = null;

        // Notifications can invoke component code, so only refresh the captures present at this point.
        foreach ((long pointerId, PointerCapture capture) in _pointerCaptures.ToArray())
        {
            if (_isDisposed)
            {
                break;
            }

            if (!_pointerCaptures.TryGetValue(pointerId, out PointerCapture? current) || !ReferenceEquals(current, capture))
            {
                continue;
            }

            try
            {
                if (!ReferenceEquals(capture.Target.InputVersion, _inputVersion))
                {
                    ReleasePointer(pointerId, true);
                }
                else
                {
                    NotifyCapture(pointerId, capture, true);
                }
            }
            catch (Exception failure)
            {
                (failures ??= new()).Add(failure);
            }
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    private sealed class PointerCapture(Widget target, Vector2 position)
    {
        internal Widget Target { get; } = target;

        internal Vector2 Position { get; set; } = position;
    }
}
