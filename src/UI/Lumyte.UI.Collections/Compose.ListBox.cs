using Lumyte.Composition;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A fully realized single-choice scrollable collection.</summary>
        /// <typeparam name="T">The application item type.</typeparam>
        [Composable]
        public partial class ListBox<T> : ItemsRepeater<T>
            where T : notnull
        {
            /// <summary>Initializes a new instance of the <see cref="ListBox{T}"/> class.</summary>
            public ListBox()
            {
                Role = UiRole.ListBox;
                Height = 160;
            }

            /// <summary>Gets or sets the declared selected ordinal key.</summary>
            [ComposeParameter]
            public string? SelectedKey { get; set; }

            /// <summary>Gets or sets the latest user selection callback.</summary>
            [ComposeParameter]
            public Action<string?>? OnSelectionChanged { get; set; }

            /// <summary>Gets or sets optional item availability; template descendants remain independent inputs.</summary>
            [ComposeParameter]
            public Func<T, bool>? ItemEnabled { get; set; }

            /// <summary>Gets or sets optional item visibility.</summary>
            [ComposeParameter]
            public Func<T, bool>? ItemVisible { get; set; }

            internal Action<string>? ItemInvoked { get; set; }

            internal Func<string?>? SelectionValue { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ListBoxElement<T>(this);
        }
    }
}
