using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Realizes all items in declaration order without selection or scrolling.</summary>
        /// <typeparam name="T">The application item type.</typeparam>
        [Composable]
        public partial class ItemsRepeater<T> : Widget
            where T : notnull
        {
            /// <summary>Gets or sets items copied at adoption.</summary>
            [ComposeParameter]
            public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

            /// <summary>Gets or sets the required ordinal key selector.</summary>
            [ComposeParameter]
            public Func<T, string>? KeySelector { get; set; }

            /// <summary>Gets or sets the required fresh content factory.</summary>
            [ComposeParameter]
            public Func<T, Widget>? ItemTemplate { get; set; }

            /// <summary>Gets or sets the realization axis.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; } = UiOrientation.Vertical;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ItemsRepeaterElement<T>(this);
        }
    }
}
