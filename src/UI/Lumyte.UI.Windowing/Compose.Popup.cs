using System.Numerics;
using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Creates passive windowing declarations through ordinary static factories.</summary>
public static partial class Compose
{
    /// <summary>Contains windowing component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares controlled popup content connected through a separate surface.</summary>
        [Composable]
        public partial class Popup : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Popup"/> class.</summary>
            public Popup()
            {
                Content = CreatePortalChildren();
            }

            /// <summary>Gets or initializes the unique key of an anchor on the owner surface.</summary>
            [ComposeParameter]
            public required string Anchor { get; init; }

            /// <summary>Gets or sets a value indicating whether the model requests an open popup.</summary>
            [ComposeParameter]
            public bool IsOpen { get; set; }

            /// <summary>Gets or sets the preferred placement.</summary>
            [ComposeParameter]
            public PopupPlacement Placement { get; set; }

            /// <summary>Gets or sets the native or hosted policy.</summary>
            [ComposeParameter]
            public PopupPresentation Presentation { get; set; }

            /// <summary>Gets the single-assignment declaration content.</summary>
            [ComposeContent]
            public WidgetChildren Content { get; }

            /// <summary>Gets or sets the model update request; no model value is changed automatically.</summary>
            [ComposeParameter]
            public Action<PopupDismissRequest>? OnDismiss { get; set; }

            /// <summary>Gets or sets the actual open notification.</summary>
            [ComposeParameter]
            public Action<UiPopupOpened>? OnOpened { get; set; }

            /// <summary>Gets or sets the notification after resources have drained.</summary>
            [ComposeParameter]
            public Action<UiPopupClosed>? OnClosed { get; set; }

            /// <inheritdoc/>
            protected override bool SupportsTransitionCapture => GetType() == typeof(Popup);

            /// <inheritdoc/>
            protected override bool CanReuseTransitionCapture(Widget previous) => GetType() == typeof(Popup);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(Widget root)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(Key);
                ArgumentException.ThrowIfNullOrWhiteSpace(Anchor);
                List<Widget> nodes = root.CollectSubtree();
                if (nodes.Count(widget => widget.Key == Anchor) > 1 || nodes.OfType<Popup>().Count(popup => popup.Key == Key) != 1)
                {
                    throw new ArgumentException("Popup keys and referenced anchors must be unique on their owner surface.", nameof(root));
                }
            }

            /// <inheritdoc/>
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

            /// <inheritdoc/>
            protected override void RecordOverride(DrawingCanvas2D canvas)
            {
            }
        }
    }
}
