using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides passive UI factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A finite one-axis viewport with optional content.</summary>
        [Composable]
        public partial class ScrollView : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="ScrollView"/> class.</summary>
            public ScrollView()
            {
                Role = UiRole.ScrollView;
                Children = CreateChildren(1);
                ClipToBounds = true;
            }

            /// <summary>Gets the optional content.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets or sets the borrowed single-viewport controller.</summary>
            [ComposeParameter]
            public ScrollController? Controller { get; set; }

            /// <summary>Gets or sets the scroll axis.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ScrollViewElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!Enum.IsDefined(Orientation) || !ClipToBounds)
                {
                    throw new ArgumentException("A scroll viewport requires a valid axis and clipping.");
                }

                if (Controller is { } controller)
                {
                    controller.ValidateAlive();
                    if (context.Nodes.OfType<ScrollView>().Count(node => ReferenceEquals(node.Controller, controller)) != 1)
                    {
                        throw new InvalidOperationException("A controller can bind to only one viewport.");
                    }
                }
            }
        }
    }
}
