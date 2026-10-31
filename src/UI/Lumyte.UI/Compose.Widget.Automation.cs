using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        public abstract partial class Widget
        {
            private string? _testId;

            /// <summary>Gets or sets an optional explicit identity; non-null values must contain non-whitespace characters.</summary>
            [ComposeParameter]
            public string? TestId
            {
                get => _testId;
                set
                {
                    if (value is not null && string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("A test identity cannot be empty or whitespace.", nameof(value));
                    }

                    _testId = value;
                }
            }

            /// <summary>Gets or sets an explicit semantic name; no text or ARIA inference is performed.</summary>
            [ComposeParameter]
            public string? AutomationName { get; set; }

            /// <summary>Gets or sets the semantic role, independently of supported actions.</summary>
            [ComposeParameter]
            public UiRole Role { get; set; }

            internal UiAutomationId AutomationId { get; private set; }

            internal IEnumerable<Widget> AutomationChildren => GetAutomationChildren();

            internal UiAutomationActions SupportedActions => AutomationActions;

            internal bool AutomationPressed => IsAutomationPressed;

            internal bool AutomationActivePress => HasAutomationActivePress;

            /// <summary>Gets actual component capabilities, independently of role.</summary>
            protected virtual UiAutomationActions AutomationActions => Focusable ? UiAutomationActions.Focus : UiAutomationActions.None;

            /// <summary>Gets a value indicating whether there is component-specific pressed presentation.</summary>
            protected virtual bool IsAutomationPressed => false;

            /// <summary>Gets a value indicating whether there is a component-owned active press, including outside capture.</summary>
            protected virtual bool HasAutomationActivePress => false;

            internal UiInputResult InvokePointerProbe(Vector2 localPosition) => OnProbePointerDown(localPosition);

            /// <summary>Returns distinct direct semantic children in preorder; the default includes all registered collections.</summary>
            /// <returns>The children exposed to UI tools.</returns>
            protected virtual IEnumerable<Widget> GetAutomationChildren() => EnumerateInputCollections();

            /// <summary>Predicts this component's pointer Down routing without side effects; input consumers override this alongside OnInput.</summary>
            /// <param name="localPosition">The input position relative to this component.</param>
            /// <returns>The same propagation decision as ordinary pointer Down.</returns>
            protected virtual UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Unhandled;
        }
    }
}
