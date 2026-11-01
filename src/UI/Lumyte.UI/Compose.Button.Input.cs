using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A declaration with a component-defined click callback.</summary>
        public partial class Button
        {
            /// <summary>Gets or sets the callback invoked synchronously after a successful press is released.</summary>
            [ComposeParameter]
            public Action? OnClick { get; set; }
        }
    }
}
