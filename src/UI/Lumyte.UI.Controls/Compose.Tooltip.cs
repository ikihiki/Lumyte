using System.Numerics;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A zero-or-one anchor with explicitly controlled passive popup content.</summary>
        [Composable]
        public partial class Tooltip : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Tooltip"/> class.</summary>
            public Tooltip()
            {
                Role = UiRole.Tooltip;
                Children = CreateChildren(1);
            }

            /// <summary>Gets the optional anchor content; its normal input remains delegated.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets or sets the borrowed popup service.</summary>
            [ComposeParameter]
            public IMenuPopupHost? PopupHost { get; set; }

            /// <summary>Gets or sets a value indicating whether the model requests passive content, without implicit hover timers or model writes.</summary>
            [ComposeParameter]
            public bool IsOpen { get; set; }

            /// <summary>Gets or sets the fresh passive content factory, called once per open.</summary>
            [ComposeParameter]
            public Func<Widget>? CreatePopup { get; set; }

            /// <summary>Gets or sets positive finite popup dimensions.</summary>
            [ComposeParameter]
            public Vector2 PopupSize { get; set; } = new(240, 80);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TooltipElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                PopupReservation.ValidateSize(PopupSize);
                if (IsOpen && (PopupHost is null || CreatePopup is null))
                {
                    throw new InvalidOperationException("An open Tooltip requires a borrowed popup host and content factory.");
                }
            }
        }
    }
}
