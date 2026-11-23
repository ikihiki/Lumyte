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
        /// <remarks>The Item slot takes precedence over ItemTemplate and receives the item and this declaration during host preparation.
        /// Each realized tree must be fresh and disjoint; fixed slot content is not cloned.</remarks>
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

            /// <summary>Gets or sets the legacy fresh content factory, used when no Item slot is supplied.</summary>
            [ComposeParameter]
            public Func<T, Widget>? ItemTemplate { get; set; }

            /// <summary>Gets or sets the realization axis.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; } = UiOrientation.Vertical;

            internal Func<T, Widget>? ItemSlot { get; set; }

            internal bool HasItemTemplate => ItemSlot is not null || ItemTemplate is not null;

            /// <inheritdoc/>
            protected override bool RebuildsChildren => true;

            internal Widget RealizeItem(T value) =>
                (ItemSlot ?? ItemTemplate)!(value) ?? throw new InvalidOperationException("An item template returned null.");

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ItemsRepeaterElement<T>(this);

            [ComposeSlot]
            private static void Item(ItemsRepeater<T> target, CompositionTemplate<T, ItemsRepeater<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (ItemsRepeater<T> next, CompositionTemplate<T, ItemsRepeater<T>, Widget> value) =>
                    next.ItemSlot = item => value(item, next));
        }
    }
}
