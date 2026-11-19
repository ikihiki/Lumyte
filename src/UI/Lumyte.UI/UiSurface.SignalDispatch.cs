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
        using UiProfiling.Scope? profile = UiProfiling.Begin(4);
        try
        {
            bool consumed;
            _dispatchDepth++;
            try
            {
                consumed = DispatchCore(input);
            }
            finally
            {
                _dispatchDepth--;
                ProcessSignals(false);
            }

            profile?.Consumed(consumed);
            return consumed;
        }
        catch (Exception error)
        {
            profile?.Fail(error);
            throw;
        }
    }

    private bool DispatchCore(UiInput input)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        if (input is UiCheckInput check)
        {
            if (!float.IsFinite(check.Position.X) || !float.IsFinite(check.Position.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            UiElement? target = FindElement(check.Target);
            if (target is null || !ReferenceEquals(target.InputVersion, _inputVersion)
                || (target.SupportedActions & UiAutomationActions.Check) == 0 || target.AutomationActivePress || target.IsTransitioning
                || !AcceptsCurrentInput(target)
                || !IsInside(target, check.Position))
            {
                return false;
            }

            for (UiElement? ancestor = target; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }
            }

            UiElement? checkHit = _rootElement.FindInputHit(check.Position, _inputVersion);
            for (UiElement? node = checkHit; node is not null; node = node.Parent)
            {
                if (node.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }

                UiInputResult probe = node.InvokePointerProbe(check.Position - node.InputOrigin);
                if (probe != UiInputResult.Unhandled)
                {
                    return ReferenceEquals(node, target) && probe == UiInputResult.Handled && DeliverInput(target, check, false) == UiInputResult.Handled;
                }
            }

            return false;
        }

        if (input is UiExpandInput expand)
        {
            if (!float.IsFinite(expand.Position.X) || !float.IsFinite(expand.Position.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            UiElement? target = FindElement(expand.Target);
            if (target is null || !ReferenceEquals(target.InputVersion, _inputVersion)
                || (target.SupportedActions & UiAutomationActions.ExpandCollapse) == 0 || target.AutomationActivePress || target.IsTransitioning
                || !AcceptsCurrentInput(target)
                || !IsInside(target, expand.Position))
            {
                return false;
            }

            for (UiElement? ancestor = target; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }
            }

            UiElement? expandHit = _rootElement.FindInputHit(expand.Position, _inputVersion);
            for (UiElement? node = expandHit; node is not null; node = node.Parent)
            {
                if (node.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }

                UiInputResult probe = node.InvokePointerProbe(expand.Position - node.InputOrigin);
                if (probe != UiInputResult.Unhandled)
                {
                    return ReferenceEquals(node, target) && probe == UiInputResult.Handled && DeliverInput(target, expand, false) == UiInputResult.Handled;
                }
            }

            return false;
        }

        if (input is UiBringItemInput bring)
        {
            if (string.IsNullOrEmpty(bring.Key) || bring.Key.Length > 1024 || !float.IsFinite(bring.Position.X) || !float.IsFinite(bring.Position.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            UiElement? target = FindElement(bring.Target);
            if (target is not IUiItemViewport items || !ReferenceEquals(target.InputVersion, _inputVersion)
                || !AcceptsCurrentInput(target) || !IsInside(target, bring.Position) || target.IsTransitioning || target.AutomationActivePress
                || (target.SupportedActions & UiAutomationActions.SelectOption) == 0)
            {
                return false;
            }

            for (UiElement? ancestor = target; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }
            }

            bool contained = false;
            for (UiElement? itemHit = _rootElement.FindInputHit(bring.Position, _inputVersion); itemHit is not null; itemHit = itemHit.Parent)
            {
                if (itemHit.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }

                contained |= ReferenceEquals(itemHit, target);
            }

            return contained && items.BringIntoView(bring.Key);
        }

        if (input is UiSetNumericInput numeric)
        {
            if (!double.IsFinite(numeric.Value) || (numeric.Upper is { } upper && (!double.IsFinite(upper) || numeric.Value > upper))
                || !float.IsFinite(numeric.Position.X) || !float.IsFinite(numeric.Position.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            UiElement? target = FindElement(numeric.Target);
            if (target is null || !ReferenceEquals(target.InputVersion, _inputVersion)
                || (target.SupportedActions & (numeric.Upper.HasValue ? UiAutomationActions.SetRange : UiAutomationActions.SetValue)) == 0 || target.AutomationActivePress || target.IsTransitioning
                || !AcceptsCurrentInput(target)
                || !IsInside(target, numeric.Position))
            {
                return false;
            }

            for (UiElement? ancestor = target; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }
            }

            UiElement? checkHit = _rootElement.FindInputHit(numeric.Position, _inputVersion);
            for (UiElement? node = checkHit; node is not null; node = node.Parent)
            {
                if (node.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
                {
                    return false;
                }

                UiInputResult probe = node.InvokePointerProbe(numeric.Position - node.InputOrigin);
                if (probe != UiInputResult.Unhandled)
                {
                    return ReferenceEquals(node, target) && probe == UiInputResult.Handled && DeliverInput(target, numeric, false) == UiInputResult.Handled;
                }
            }

            return false;
        }

        if (input is UiFocusLostInput)
        {
            _panCandidates.Clear();
            bool cancelled = CancelAllInput();
            return (!_isDisposed && RouteInput(_rootElement, input)) || cancelled;
        }

        if (input is UiKeyCancelInput keyCancel)
        {
            bool consumed = _consumedKeys.Contains(keyCancel.Key);
            return CancelKey(keyCancel.Key) || consumed;
        }

        if (input is UiCommittedTextInput text)
        {
            ArgumentNullException.ThrowIfNull(text.Text);
            return _focusedElement is { } focused && IsFocusEligible(focused) && RouteInput(focused, text);
        }

        if (input is UiKeyInput key)
        {
            return DispatchKey(key);
        }

        if (input is UiWheelInput wheel)
        {
            if (!float.IsFinite(wheel.Position.X) || !float.IsFinite(wheel.Position.Y)
                || !float.IsFinite(wheel.Delta.X) || !float.IsFinite(wheel.Delta.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            UiElement? wheelHit = HitTest(wheel.Position);
            Vector2 residual = wheel.Delta;
            bool consumed = false;
            for (UiElement? node = wheelHit; node is not null; node = node.Parent)
            {
                if (node is ScrollViewElement scroll && AcceptsCurrentInput(scroll))
                {
                    bool vertical = scroll.Widget.Orientation == UiOrientation.Vertical;
                    float delta = vertical ? residual.Y : residual.X;
                    float remainder = scroll.ApplyDelta(delta);
                    if (_isDisposed || !ReferenceEquals(scroll.Surface, this))
                    {
                        return true;
                    }

                    consumed |= remainder != delta;
                    residual = vertical ? new(residual.X, remainder) : new(remainder, residual.Y);
                }
                else if (DeliverInput(node, wheel with { Delta = residual }, false) != UiInputResult.Unhandled)
                {
                    return true;
                }

                if (_isDisposed || !ReferenceEquals(node.Surface, this))
                {
                    return consumed;
                }
            }

            return consumed;
        }

        if (input is not UiPointerInput pointer)
        {
            return RouteInput(_focusedElement ?? _rootElement, input);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pointer.PointerId);
        if (!Enum.IsDefined(pointer.Device))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!float.IsFinite(pointer.Position.X) || !float.IsFinite(pointer.Position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        try
        {
            if (RoutePan(pointer, out bool panConsumed))
            {
                return panConsumed;
            }
        }
        catch (Exception failure)
        {
            _panCandidates.Remove(pointer.PointerId);
            try
            {
                CancelPointer(pointer.PointerId);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }

            throw;
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
        handled |= pointer.Phase == UiPointerPhase.Down && _panCandidates.ContainsKey(pointer.PointerId);
        if (!_isDisposed && pointer.Phase == UiPointerPhase.Down && _focusChange == focusChange && _focusRequest == focusRequest
            && (!handled || _pointerCaptures.ContainsKey(pointer.PointerId)))
        {
            ChangeFocus(null);
        }

        return handled;
    }
}
