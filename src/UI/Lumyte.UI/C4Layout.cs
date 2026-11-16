using System.Numerics;
using static Lumyte.UI.Compose.Definitions;

namespace Lumyte.UI;

internal static class C4Layout
{
    internal static void Validate(Widget widget, UiWidgetValidationContext context)
    {
        if (widget is Flex flex)
        {
            ValidateOrientation(flex.Orientation);
        }

        if (widget is Wrap wrap)
        {
            ValidateOrientation(wrap.Orientation);
        }

        if (widget is Expanded && (context.GetParent(widget) is not Flex parentFlex || !context.GetSlotChildren(parentFlex.Children).Contains(widget)))
        {
            throw new InvalidOperationException("Expanded requires direct ordinary Flex content.");
        }

        if (widget is GridCell && (context.GetParent(widget) is not Grid parentGrid || !context.GetSlotChildren(parentGrid.Children).Contains(widget)))
        {
            throw new InvalidOperationException("GridCell requires direct ordinary Grid content.");
        }

        if (widget is Grid grid)
        {
            var cells = new List<GridCell>();
            var ordinary = new HashSet<Widget>(context.GetSlotChildren(grid.Children), ReferenceEqualityComparer.Instance);
            foreach (Widget child in context.GetChildren(grid))
            {
                if (child is not GridCell cell || !ordinary.Contains(child))
                {
                    throw new InvalidOperationException("Grid accepts only explicit GridCell content.");
                }

                if (cell.Row >= grid.Rows.Count || cell.RowSpan > grid.Rows.Count - cell.Row
                    || cell.Column >= grid.Columns.Count || cell.ColumnSpan > grid.Columns.Count - cell.Column)
                {
                    throw new InvalidOperationException("GridCell exceeds declared tracks.");
                }

                foreach (GridCell previous in cells)
                {
                    if ((long)cell.Row < (long)previous.Row + previous.RowSpan && (long)previous.Row < (long)cell.Row + cell.RowSpan
                        && (long)cell.Column < (long)previous.Column + previous.ColumnSpan && (long)previous.Column < (long)cell.Column + cell.ColumnSpan)
                    {
                        throw new InvalidOperationException("GridCell ranges overlap, including hidden cells.");
                    }
                }

                cells.Add(cell);
            }
        }
    }

    internal static float Main(Vector2 size, bool vertical) => vertical ? size.Y : size.X;

    internal static float Cross(Vector2 size, bool vertical) => vertical ? size.X : size.Y;

    internal static Vector2 Vector(double main, double cross, bool vertical) => vertical
        ? new(UiLayoutMath.FiniteFloat(cross), UiLayoutMath.FiniteFloat(main))
        : new(UiLayoutMath.FiniteFloat(main), UiLayoutMath.FiniteFloat(cross));

    internal static UiBoxConstraints Constraints(float minimum, float maximum, float cross, bool vertical) => vertical
        ? new(0, cross, minimum, maximum)
        : new(minimum, maximum, 0, cross);

    internal static void ValidateOrientation(UiOrientation orientation)
    {
        if (orientation is not UiOrientation.Vertical and not UiOrientation.Horizontal)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }
    }
}
