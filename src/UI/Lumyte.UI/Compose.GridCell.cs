using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares GridCell layout without owning runtime geometry.</summary>
        [Composable]
        public partial class GridCell : SizedBox
        {
            private int _row = 0;
            private int _column = 0;
            private int _rowSpan = 1;
            private int _columnSpan = 1;

            /// <summary>Initializes a new instance of the <see cref="GridCell"/> class.</summary>
            public GridCell()
            {
            }

            /// <summary>Gets or sets the nonnegative row index, initially zero.</summary>
            [ComposeParameter]
            public int Row
            {
                get => _row;
                set
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(value);
                    _row = value;
                }
            }

            /// <summary>Gets or sets the nonnegative column index, initially zero.</summary>
            [ComposeParameter]
            public int Column
            {
                get => _column;
                set
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(value);
                    _column = value;
                }
            }

            /// <summary>Gets or sets the positive row span, initially one.</summary>
            [ComposeParameter]
            public int RowSpan
            {
                get => _rowSpan;
                set
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                    _rowSpan = value;
                }
            }

            /// <summary>Gets or sets the positive column span, initially one.</summary>
            [ComposeParameter]
            public int ColumnSpan
            {
                get => _columnSpan;
                set
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                    _columnSpan = value;
                }
            }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new C4BoxElement<GridCell>(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => C4Layout.Validate(this, context);
        }
    }
}
