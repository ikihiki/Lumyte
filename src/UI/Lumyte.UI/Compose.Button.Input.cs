using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container with one content child and an ordered background slot.</summary>
        public partial class Button
        {
            private long? _pressedPointer;

            /// <summary>Gets a value indicating whether a captured pointer is inside the current button geometry.</summary>
            public bool IsPressed { get; private set; }

            /// <summary>Gets or sets the callback invoked synchronously after a successful press is released.</summary>
            [ComposeParameter]
            public Action? OnClick { get; set; }

            /// <inheritdoc/>
            protected override bool RetainsInputOnRebuild => true;

            /// <inheritdoc/>
            protected override void OnReconcile(Widget previous)
            {
                var button = (Button)previous;
                _pressedPointer = button._pressedPointer;
                IsPressed = button.IsPressed;
            }

            /// <inheritdoc/>
            protected override UiInputResult OnInput(in UiInputContext context)
            {
                if (context.Input is not UiPointerInput pointer)
                {
                    return UiInputResult.Unhandled;
                }

                switch (pointer.Phase)
                {
                    case UiPointerPhase.Down:
                        if (!context.IsInside)
                        {
                            return UiInputResult.StopPropagation;
                        }

                        if (_pressedPointer is not null)
                        {
                            return UiInputResult.Handled;
                        }

                        return context.CapturePointer() ? UiInputResult.Handled : UiInputResult.StopPropagation;
                    case UiPointerPhase.Move when context.IsCaptured:
                        IsPressed = context.IsInside;
                        return UiInputResult.Handled;
                    case UiPointerPhase.Up when context.IsCaptured:
                        if (context.IsInside)
                        {
                            Action? callback = OnClick;
                            callback?.Invoke();
                        }

                        return UiInputResult.Handled;
                    case UiPointerPhase.Cancel when context.IsCaptured:
                        return UiInputResult.Handled;
                    default:
                        return UiInputResult.Unhandled;
                }
            }

            /// <inheritdoc/>
            protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
            {
                _pressedPointer = state.IsCaptured ? state.PointerId : null;
                IsPressed = state.IsCaptured && state.IsInside;
            }

            /// <inheritdoc/>
            protected override IEnumerable<Widget> GetInputChildren() => Children;
        }
    }
}
