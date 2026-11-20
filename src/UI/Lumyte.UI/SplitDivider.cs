using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using PhysicalKey = Lumyte.Input.Abstractions.Key;

namespace Lumyte.UI;

internal sealed class SplitDivider : Compose.Definitions.Widget
{
    internal int Index { get; init; }

    internal string Signature { get; init; } = string.Empty;

    protected override UiElement CreateElement() => new DividerElement(this);

    internal sealed class DividerElement(SplitDivider widget) : UiElement<SplitDivider>(widget)
    {
        private long? _pointer;
        private PhysicalKey? _key;
        private bool _gesture;
        private float _last;

        protected override bool PreservePointerGesture => true;

        protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.SetValue;

        protected override double? AutomationValue => Owner?.Fraction(Widget.Index) ?? 0;

        protected override UiNumericState? NumericState => new(0, 1, null, AutomationValue ?? 0, null, null);

        protected override bool HasAutomationActivePress => _gesture;

        protected override bool CanReceiveFocus => base.CanReceiveFocus && ContentSize.X > 0 && ContentSize.Y > 0;

        private SplitViewElement? Owner => Parent as SplitViewElement;

        internal void CancelOwnership()
        {
            long? pointer = _pointer;
            PhysicalKey? key = _key;
            Finish(false);
            if (pointer is { } id)
            {
                Surface?.CancelPointer(id, this);
            }

            if (key is { } held)
            {
                Surface?.CancelKey(held, this);
            }
        }

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.Unhandled;

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            SplitViewElement? owner = Owner;
            if (owner is null)
            {
                return UiInputResult.Unhandled;
            }

            if (context.Input is UiSetNumericInput numeric)
            {
                if (numeric.Upper is not null || !owner.Begin(this))
                {
                    return UiInputResult.StopPropagation;
                }

                try
                {
                    context.RequestFocus();
                    owner.ResizeFraction(Widget.Index, numeric.Value);
                }
                finally
                {
                    owner.End(this, false);
                }

                return UiInputResult.Handled;
            }

            if (context.Input is UiKeyCancelInput or UiFocusChangedInput { IsFocused: false })
            {
                Finish(false);
                return UiInputResult.Unhandled;
            }

            if (context.Input is UiPointerInput pointer)
            {
                float axis = owner.Widget.Orientation == UiOrientation.Horizontal ? pointer.Position.X : pointer.Position.Y;
                if (pointer.Phase == UiPointerPhase.Down && context.IsInside && !_gesture && owner.Begin(this))
                {
                    if (!context.CapturePointer())
                    {
                        owner.End(this, false);
                        return UiInputResult.StopPropagation;
                    }

                    _gesture = true;
                    _pointer = pointer.PointerId;
                    _last = axis;
                    context.RequestFocus();
                    return UiInputResult.Handled;
                }

                if (context.IsCaptured)
                {
                    if (pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel)
                    {
                        Finish(pointer.Phase == UiPointerPhase.Up);
                        return UiInputResult.Handled;
                    }

                    if (pointer.Phase == UiPointerPhase.Move && _gesture)
                    {
                        double movement = (double)axis - _last;
                        _last = axis;
                        owner.Resize(Widget.Index, movement);
                        return UiInputResult.Handled;
                    }
                }

                return UiInputResult.Unhandled;
            }

            if (context.Input is not UiKeyInput key)
            {
                return UiInputResult.Unhandled;
            }

            if (key.IsDown && key.Key == PhysicalKey.Escape && _gesture)
            {
                long? ownedPointer = _pointer;
                PhysicalKey? captured = _key;
                Finish(false);
                if (ownedPointer is { } id)
                {
                    context.ReleasePointerCapture(id);
                }

                if (captured is { } held)
                {
                    context.ReleaseKeyCapture(held);
                }

                return UiInputResult.Handled;
            }

            if (!key.IsDown)
            {
                if (context.IsKeyCaptured && _key == key.Key && _gesture)
                {
                    Finish(true);
                    return UiInputResult.Handled;
                }

                return UiInputResult.Unhandled;
            }

            bool vertical = owner.Widget.Orientation == UiOrientation.Vertical;
            bool navigation = key.Key is PhysicalKey.Home or PhysicalKey.End || (vertical ? key.Key is PhysicalKey.ArrowUp or PhysicalKey.ArrowDown : key.Key is PhysicalKey.ArrowLeft or PhysicalKey.ArrowRight);
            if (!navigation || !context.IsFocused || _pointer is not null || (_key is not null && _key != key.Key) || (key.Modifiers & (UiModifiers.Alt | UiModifiers.Meta)) != 0)
            {
                return UiInputResult.Unhandled;
            }

            if (key.IsRepeat)
            {
                if (!context.IsKeyCaptured || _key != key.Key)
                {
                    return UiInputResult.Unhandled;
                }
            }
            else
            {
                if (!owner.Begin(this))
                {
                    return UiInputResult.Unhandled;
                }

                if (!context.CaptureKey())
                {
                    owner.End(this, false);
                    return UiInputResult.Unhandled;
                }
            }

            _gesture = true;
            _key = key.Key;
            double step = key.Modifiers == UiModifiers.None ? owner.Widget.SmallStep : owner.Widget.LargeStep;
            double delta = key.Key switch
            {
                PhysicalKey.Home => -double.MaxValue,
                PhysicalKey.End => double.MaxValue,
                PhysicalKey.ArrowLeft or PhysicalKey.ArrowUp => -step,
                _ => step,
            };
            owner.Resize(Widget.Index, delta);
            return UiInputResult.Handled;
        }

        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            if (!state.IsCaptured && state.PointerId == _pointer)
            {
                _pointer = null;
            }
        }

        protected override void OnUnmounted() => Finish(false);

        protected override Action? PrepareUpdate(SplitDivider next, UiElementUpdateContext context)
        {
            if (next.Index != Widget.Index || next.Signature != Widget.Signature || Owner?.Widget.IsEnabled == false)
            {
                context.CancelInput();
            }

            return null;
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            if (Owner is { } owner)
            {
                bool vertical = owner.Widget.Orientation == UiOrientation.Vertical;
                float extent = Math.Min(owner.Widget.DividerThickness, vertical ? ContentSize.Y : ContentSize.X);
                canvas.FillRect(vertical ? new(0, (ContentSize.Y - extent) / 2, ContentSize.X, extent) : new((ContentSize.X - extent) / 2, 0, extent, ContentSize.Y), owner.Widget.DividerColor);
            }
        }

        private void Finish(bool commit)
        {
            bool complete = _gesture;
            _gesture = false;
            _pointer = null;
            _key = null;
            Owner?.End(this, complete && commit);
        }
    }
}
