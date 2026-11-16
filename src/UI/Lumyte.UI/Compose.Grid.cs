using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares Grid layout without owning runtime geometry.</summary>
        [Composable]
        public partial class Grid : Panel
        {
            private IReadOnlyList<UiGridTrack> _rows = Array.AsReadOnly(new[] { new UiGridTrack(1, true) });
            private IReadOnlyList<UiGridTrack> _columns = Array.AsReadOnly(new[] { new UiGridTrack(1, true) });
            private float _rowSpacing = 0;
            private float _columnSpacing = 0;

            /// <summary>Initializes a new instance of the <see cref="Grid"/> class.</summary>
            public Grid()
            {
            }

            /// <summary>Gets or sets the row tracks, copied on assignment; initially one ratio track.</summary>
            [ComposeParameter]
            public IReadOnlyList<UiGridTrack> Rows
            {
                get => _rows;
                set => _rows = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
            }

            /// <summary>Gets or sets the column tracks, copied on assignment; initially one ratio track.</summary>
            [ComposeParameter]
            public IReadOnlyList<UiGridTrack> Columns
            {
                get => _columns;
                set => _columns = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
            }

            /// <summary>Gets or sets the finite nonnegative row gap, initially zero.</summary>
            [ComposeParameter]
            public float RowSpacing
            {
                get => _rowSpacing;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    _rowSpacing = value;
                }
            }

            /// <summary>Gets or sets the finite nonnegative column gap, initially zero.</summary>
            [ComposeParameter]
            public float ColumnSpacing
            {
                get => _columnSpacing;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    _columnSpacing = value;
                }
            }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new GridElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => C4Layout.Validate(this, context);
        }
    }
}
