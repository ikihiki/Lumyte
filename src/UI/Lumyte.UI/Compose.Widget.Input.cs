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
            /// <summary>Gets or sets a value indicating whether this node and its descendants accept input.</summary>
            [ComposeParameter]
            public bool IsEnabled { get; set; } = true;

            /// <summary>Gets or sets a value indicating whether this subtree participates in pointer and focus traversal.</summary>
            [ComposeParameter]
            public bool HitTestVisible { get; set; } = true;
        }
    }
}
