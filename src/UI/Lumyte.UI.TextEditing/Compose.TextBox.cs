using Lumyte.Composition;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Provides static editor construction functions.</summary>
public static partial class Compose
{
    /// <summary>Contains passive component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>A controlled single-line editor declaration.</summary>
        [Composable]
        public partial class TextBox : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="TextBox"/> class.</summary>
            public TextBox()
            {
                Focusable = true;
                Role = UiRole.TextBox;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the controlled committed text.</summary>
            [ComposeParameter]
            public string Text { get; set; } = string.Empty;

            /// <summary>Gets or sets the borrowed layout provider.</summary>
            [ComposeParameter]
            public IUiTextLayoutProvider? LayoutProvider { get; set; }

            /// <summary>Gets or sets a value indicating whether editing is disabled.</summary>
            [ComposeParameter]
            public bool IsReadOnly { get; set; }

            /// <summary>Gets or sets the committed-text update callback.</summary>
            [ComposeParameter]
            public Action<UiTextChange>? OnTextChanged { get; set; }

            /// <summary>Gets or sets the preedit update callback.</summary>
            [ComposeParameter]
            public Action<TextCompositionSnapshot?>? OnCompositionChanged { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TextBoxElement(this);
        }
    }
}
