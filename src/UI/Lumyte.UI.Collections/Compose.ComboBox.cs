using System.Numerics;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A non-editable single-choice popup selector.</summary>
        /// <typeparam name="T">The application item type.</typeparam>
        [Composable]
        public partial class ComboBox<T> : ListBox<T>
            where T : notnull
        {
            /// <summary>Initializes a new instance of the <see cref="ComboBox{T}"/> class.</summary>
            public ComboBox()
            {
                Role = UiRole.ComboBox;
                Focusable = true;
                Height = 32;
            }

            /// <summary>Gets or sets the borrowed popup host.</summary>
            [ComposeParameter]
            public ICollectionPopupHost? PopupHost { get; set; }

            /// <summary>Gets or sets fresh closed-display content; null selection uses EmptyTemplate.</summary>
            [ComposeParameter]
            public Func<T, Widget>? SelectedItemTemplate { get; set; }

            /// <summary>Gets or sets a fresh empty-display factory.</summary>
            [ComposeParameter]
            public Func<Widget>? EmptyTemplate { get; set; }

            /// <summary>Gets or sets the explicit positive popup viewport.</summary>
            [ComposeParameter]
            public Vector2 PopupSize { get; set; } = new(200, 160);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ComboBoxElement<T>(this);
        }
    }
}
