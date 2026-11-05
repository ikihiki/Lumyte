using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media declaration factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Displays a prepared borrowed image without loading or owning resources.</summary>
        [Composable]
        public partial class Image : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Image"/> class.</summary>
            public Image()
            {
                Role = UiRole.Image;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the borrowed prepared snapshot, or null while unavailable.</summary>
            [ComposeParameter]
            public PreparedImage? Source { get; set; }

            /// <summary>Gets or sets the finite nonnegative occupied size when the source is absent.</summary>
            [ComposeParameter]
            public Vector2 PlaceholderSize { get; set; }

            /// <summary>Gets or sets centered stretch; the default preserves aspect ratio inside the body.</summary>
            [ComposeParameter]
            public ImageStretch Stretch { get; set; } = ImageStretch.Uniform;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ImageElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                MediaGeometry.Nonnegative(PlaceholderSize, nameof(PlaceholderSize));
                if (!Enum.IsDefined(Stretch))
                {
                    throw new ArgumentOutOfRangeException(nameof(Stretch));
                }
            }
        }
    }
}
