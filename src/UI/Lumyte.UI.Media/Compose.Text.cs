using Lumyte.Composition;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media construction functions.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Displays immutable, width-dependent text without an editing document.</summary>
        [Composable]
        public partial class Text : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Text"/> class.</summary>
            public Text()
            {
                Role = UiRole.Text;
            }

            /// <summary>Gets or sets the plain text; null is not permitted.</summary>
            [ComposeParameter]
            public string Value { get; set; } = string.Empty;

            /// <summary>Gets or sets the borrowed provider, required when connecting the declaration.</summary>
            [ComposeParameter]
            public ITextLayoutProvider? LayoutProvider { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TextElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                ArgumentNullException.ThrowIfNull(Value);
                if (LayoutProvider is null)
                {
                    throw new InvalidOperationException("Text requires an explicit layout provider.");
                }
            }
        }
    }
}
