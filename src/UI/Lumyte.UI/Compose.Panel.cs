using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container owning an ordered list of ordinary children.</summary>
        [Composable]
        public partial class Panel : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Panel"/> class.</summary>
            public Panel()
                : base(int.MaxValue)
            {
            }

            /// <summary>Gets the ordinary children, initialized at most once.</summary>
            [ComposeContent]
            public WidgetChildren Children => ContentCollection;

            /// <inheritdoc/>
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Vector2 contentSize = Vector2.Zero;
                foreach (Widget child in Children)
                {
                    Vector2 size = LayoutChild(child, constraints.Loosen());
                    contentSize = Vector2.Max(contentSize, size);
                    SetChildOffset(child, Vector2.Zero);
                }

                return UiLayoutMath.FillBounded(constraints, contentSize.X, contentSize.Y);
            }
        }
    }
}
