using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class DataGridHeader : Widget
{
    internal DataGridHeader()
    {
        Role = UiRole.Button;
        Focusable = true;
        ClipToBounds = true;
        Content = CreateChildren(1);
    }

    internal WidgetChildren Content { get; }

    internal required Action Sort { get; init; }

    internal bool CanSort { get; init; }

    internal required Action<float> Resize { get; init; }

    internal required Func<float> AcceptedWidth { get; init; }

    protected override UiElement CreateElement() => new HeaderElement(this);

    private sealed class HeaderElement(DataGridHeader widget) : UiElement<DataGridHeader>(widget)
    {
        private readonly UiActivationRecognizer _activation = new();
        private bool _resizing;
        private long? _resizePointer;
        private float _startX;
        private float _startWidth;

        protected override Lumyte.Graphics.Drawing2D.Rect2D AutomationActionRegion => new(0, 0, Math.Max(0, ContentSize.X - 8), ContentSize.Y);

        protected override UiAutomationActions AutomationActions => UiAutomationActions.Focus | (Widget.CanSort && ContentSize.X > 8 ? UiAutomationActions.Click : UiAutomationActions.None);

        protected override bool IsAutomationPressed => _resizing || _activation.IsPressed;

        protected override bool HasAutomationActivePress => _resizing || _activation.HasActivePress;

        protected override bool PreservePointerGesture => _resizing;

        protected override IEnumerable<UiElement> GetInputChildren() => Array.Empty<UiElement>();

        protected override IEnumerable<UiElement> GetAutomationChildren() => Array.Empty<UiElement>();

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down } press && context.PointerPosition is { } point && point.X >= ContentSize.X - 8)
            {
                if (context.CapturePointer())
                {
                    _resizing = true;
                    _resizePointer = press.PointerId;
                    _startX = point.X;
                    _startWidth = Widget.AcceptedWidth();
                    context.RequestFocus();
                }

                return UiInputResult.Handled;
            }

            if (context.Input is UiPointerInput pointer && context.IsCaptured && _resizePointer == pointer.PointerId)
            {
                if ((pointer.Phase is UiPointerPhase.Move or UiPointerPhase.Up) && context.PointerPosition is { } move)
                {
                    Widget.Resize((float)Math.Clamp((double)_startWidth + move.X - _startX, -float.MaxValue, float.MaxValue));
                }

                if (pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel)
                {
                    _resizePointer = null;
                    _resizing = false;
                }

                return UiInputResult.Handled;
            }

            if (context.IsFocused && context.Input is UiKeyInput { IsDown: true, Modifiers: UiModifiers.None, Key: Lumyte.Input.Abstractions.Key.ArrowLeft or Lumyte.Input.Abstractions.Key.ArrowRight } key)
            {
                Widget.Resize(Widget.AcceptedWidth() + (key.Key == Lumyte.Input.Abstractions.Key.ArrowLeft ? -8 : 8));
                return UiInputResult.Handled;
            }

            if (!Widget.CanSort)
            {
                return context.Input is UiPointerInput ? UiInputResult.StopPropagation : UiInputResult.Unhandled;
            }

            bool? sortInside = context.PointerPosition is { } sortPoint ? context.IsInside && sortPoint.X < Math.Max(0, ContentSize.X - 8) : null;
            return _activation.HandleInput(in context, context.PointerPosition is { } down ? OnProbePointerDown(down) : UiInputResult.Unhandled, Widget.Sort, sortInside);
        }

        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            _activation.PointerCaptureChanged(state);
            if (!state.IsCaptured)
            {
                _resizing = false;
            }
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = LayoutChild(Children[0], constraints);
            SetChildOffset(Children[0], Vector2.Zero);
            return constraints.Constrain(size);
        }
    }
}
