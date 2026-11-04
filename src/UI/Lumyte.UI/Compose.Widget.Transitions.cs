using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration with construction-time child ownership.</summary>
        public abstract partial class Widget
        {
            /// <summary>Gets or initializes the optional shared identity within one navigation slot.</summary>
            [ComposeParameter]
            public UiSharedElementKey? SharedElementKey { get; init; }

            /// <summary>Gets or initializes immutable presence effects.</summary>
            [ComposeParameter]
            public UiElementTransitions? Transitions { get; init; }

            /// <summary>Gets or sets a value indicating whether the model requests presence.</summary>
            [ComposeParameter]
            public bool IsPresent { get; set; } = true;

            /// <summary>Gets or sets the notification after final entry presentation.</summary>
            [ComposeParameter]
            public Action? OnEntered { get; set; }

            /// <summary>Gets or sets the notification after final exit presentation.</summary>
            [ComposeParameter]
            public Action? OnExited { get; set; }
        }
    }
}
