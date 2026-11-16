using Lumyte.Composition;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media configurations.</summary>
    public static partial class Definitions
    {
        /// <summary>A noninteractive icon; set AutomationName explicitly for meaningful content.</summary>
        [Composable]
        public partial class Icon : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Icon"/> class.</summary>
            public Icon()
            {
                Role = UiRole.Icon;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the immutable representation, or null for an empty occupied icon.</summary>
            [ComposeParameter]
            public IconSource? Source { get; set; }

            /// <summary>Gets or sets the positive finite requested square dimension; default 16.</summary>
            [ComposeParameter]
            public float Size { get; set; } = 16;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new IconElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => MediaGeometry.Positive(Size, nameof(Size));
        }
    }
}
