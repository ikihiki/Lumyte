using System.Numerics;

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
            /// <inheritdoc/>
            protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.Click;

            /// <inheritdoc/>
            protected override bool IsAutomationPressed => IsPressed;

            /// <inheritdoc/>
            protected override bool HasAutomationActivePress => _pressedPointer is not null || _pressedKey is not null;

            /// <inheritdoc/>
            protected override IEnumerable<Widget> GetAutomationChildren() => Children;

            /// <inheritdoc/>
            protected override UiInputResult OnProbePointerDown(Vector2 localPosition) =>
                InputBounds.Contains(localPosition + InputOrigin) ? UiInputResult.Handled : UiInputResult.StopPropagation;
        }
    }
}
