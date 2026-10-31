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
            private bool _pointerInside;

            /// <summary>Gets a value indicating whether this button owns a key press or its captured pointer is inside the current geometry.</summary>
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
                _pointerInside = button._pointerInside;
                _pressedKey = button._pressedKey;
                IsPressed = button.IsPressed;
            }

            /// <inheritdoc/>
            protected override UiInputResult OnInput(in UiInputContext context)
            {
                if (context.Input is not UiPointerInput pointer)
                {
                    return HandleKeyInput(in context);
                }

                switch (pointer.Phase)
                {
                    case UiPointerPhase.Down:
                        UiInputResult disposition = OnProbePointerDown(pointer.Position - InputOrigin);
                        if (disposition != UiInputResult.Handled)
                        {
                            return disposition;
                        }

                        if (_pressedPointer is not null || _pressedKey is not null)
                        {
                            return UiInputResult.Handled;
                        }

                        if (!context.CapturePointer())
                        {
                            return UiInputResult.StopPropagation;
                        }

                        context.RequestFocus();
                        return UiInputResult.Handled;
                    case UiPointerPhase.Move when context.IsCaptured:
                        _pointerInside = context.IsInside;
                        UpdatePressedState();
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
                _pointerInside = state.IsCaptured && state.IsInside;
                UpdatePressedState();
            }

            /// <inheritdoc/>
            protected override IEnumerable<Widget> GetInputChildren() => Children;
        }
    }
}
