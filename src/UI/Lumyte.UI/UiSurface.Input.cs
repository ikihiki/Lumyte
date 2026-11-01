using System.Numerics;
using System.Runtime.ExceptionServices;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<long, PointerCapture> _pointerCaptures = new();
    private object? _inputVersion;
    private bool _inputDirty = true;

    /// <summary>Routes logical input, returning whether this surface consumed it.</summary>
    /// <param name="input">The host-selected input.</param>
    /// <remarks>Call UpdateLayout before normal input delivery. Dispatch never updates geometry.</remarks>
    /// <returns>Whether a widget consumed the input or a captured series was delivered.</returns>
    public bool Dispatch(UiInput input)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        if (input is UiFocusLostInput)
        {
            bool cancelled = CancelAllPointers();
            return (!_isDisposed && RouteInput(_rootElement, input)) || cancelled;
        }

        if (input is not UiPointerInput pointer)
        {
            return RouteInput(_rootElement, input);
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
            return false;
        }

        UiElement? hit = _rootElement.FindInputHit(pointer.Position, _inputVersion);
        return hit is not null && RouteInput(hit, pointer);
    }

    internal void InvalidateInput() => _inputDirty = true;

    internal bool IsInside(UiElement target, Vector2 position) =>
        ReferenceEquals(target.InputVersion, _inputVersion) && target.InputBounds.Contains(position);

    internal bool CapturePointer(UiElement target, UiInput input)
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

    internal bool CancelPointer(long pointerId, UiElement? expected = null)
    {
        if (!_pointerCaptures.TryGetValue(pointerId, out PointerCapture? capture)
            || (expected is not null && !ReferenceEquals(capture.Target, expected)))
        {
            return false;
        }

        ReleasePointer(pointerId, true);
        return true;
    }

    private bool RouteInput(UiElement target, UiInput input)
    {
        for (UiElement? node = target; node is not null; node = node.Parent)
        {
            UiInputResult result = DeliverInput(node, input, false);
            if (input is UiPointerInput pointer && _pointerCaptures.ContainsKey(pointer.PointerId))
            {
                return true;
            }

            if (result != UiInputResult.Unhandled)
            {
                return result == UiInputResult.Handled;
            }

            if (_isDisposed || !ReferenceEquals(node.Surface, this))
            {
                return false;
            }
        }

        return false;
    }

    private UiInputResult DeliverInput(UiElement target, UiInput input, bool isCaptured)
    {
        var context = new UiInputContext(this, target, input, isCaptured);
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

    private bool CancelAllPointers()
    {
        if (_pointerCaptures.Count == 0)
        {
            return false;
        }

        KeyValuePair<long, PointerCapture>[] captures = _pointerCaptures.ToArray();
        _pointerCaptures.Clear();
        List<Exception>? failures = null;
        foreach ((long pointerId, PointerCapture capture) in captures)
        {
            try
            {
                NotifyReleased(pointerId, capture, true);
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

        return true;
    }

    private void UpdateInput()
    {
        if (!_inputDirty)
        {
            return;
        }

        _inputVersion = new object();
        _rootElement.UpdateInput(Vector2.Zero, UiInputBounds.From(Vector2.Zero, _viewport), _inputVersion);
        _inputDirty = false;

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

    private sealed class PointerCapture(UiElement target, Vector2 position)
    {
        internal UiElement Target { get; } = target;

        internal Vector2 Position { get; set; } = position;
    }
}
