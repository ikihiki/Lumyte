using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains declarations consumed by Composition.</summary>
    public static partial class Definitions
    {
        /// <summary>A box with optional explicit body dimensions and at most one child.</summary>
        [Composable]
        public partial class SizedBox : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="SizedBox"/> class.</summary>
            public SizedBox()
            {
                Children = CreateChildren(1);
            }

            /// <summary>Gets the optional, single-assignment content.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new SizedBoxElement(this);
        }
    }
}
