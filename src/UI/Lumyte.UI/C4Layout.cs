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
            ValidateAlignment(flex.MainAxisAlignment, flex.CrossAxisAlignment);
        }

        if (widget is Wrap wrap)
        {
            ValidateOrientation(wrap.Orientation);
            ValidateAlignment(wrap.MainAxisAlignment, wrap.CrossAxisAlignment);
            if (!Enum.IsDefined(wrap.RunAlignment) || wrap.CrossAxisAlignment == UiCrossAxisAlignment.Stretch)
            {
                throw new ArgumentOutOfRangeException(nameof(wrap));
            }
        }

        if (widget is Grid grid)
        {
            var cells = new List<UiParentData>();
            IReadOnlyList<Widget> ordinary = context.GetSlotChildren(grid.Children);
            if (context.GetChildren(grid).Count != ordinary.Count)
            {
                throw new InvalidOperationException("Grid requires ordinary content only.");
            }

            foreach (Widget child in ordinary)
            {
                if (child.ParentData.Length != 1 || child.ParentData[0].Kind != UiParentDataKind.Grid)
                {
                    throw new InvalidOperationException("Grid accepts only explicit Grid.Cell content.");
                }

                UiParentData cell = child.ParentData[0];
                if (cell.Row >= grid.Rows.Count || cell.RowSpan > grid.Rows.Count - cell.Row
                    || cell.Column >= grid.Columns.Count || cell.ColumnSpan > grid.Columns.Count - cell.Column)
                {
                    throw new InvalidOperationException("Grid.Cell exceeds declared tracks.");
                }

                foreach (UiParentData previous in cells)
                {
                    if ((long)cell.Row < (long)previous.Row + previous.RowSpan && (long)previous.Row < (long)cell.Row + cell.RowSpan
                        && (long)cell.Column < (long)previous.Column + previous.ColumnSpan && (long)previous.Column < (long)cell.Column + cell.ColumnSpan)
                    {
                        throw new InvalidOperationException("Grid.Cell ranges overlap, including hidden cells.");
                    }
                }

                cells.Add(cell);
            }
        }
    }

    internal static void ValidateParentData(Widget widget, UiWidgetValidationContext context)
    {
        if (widget.ParentData.Length == 0)
        {
            return;
        }

        if (widget.ParentData.Length != 1)
        {
            throw new InvalidOperationException("A child accepts exactly one parent placement operation.");
        }

        if (!context.IsOrdinaryChild(widget, widget.ParentData[0].Kind))
        {
            throw new InvalidOperationException("Placement requires the matching direct ordinary parent slot.");
        }
    }

    internal static (double Start, double Gap) Distribution(UiMainAxisAlignment alignment, double free, int count)
    {
        free = Math.Max(0, free);
        return alignment switch
        {
            UiMainAxisAlignment.Center => (free / 2, 0),
            UiMainAxisAlignment.End => (free, 0),
            UiMainAxisAlignment.SpaceBetween when count > 1 => (0, free / (count - 1)),
            UiMainAxisAlignment.SpaceAround when count > 0 => (free / count / 2, free / count),
            UiMainAxisAlignment.SpaceEvenly when count > 0 => (free / (count + 1), free / (count + 1)),
            _ => (0, 0),
        };
    }

    internal static double CrossOffset(UiCrossAxisAlignment alignment, double free) => alignment switch
    {
        UiCrossAxisAlignment.Center => Math.Max(0, free) / 2,
        UiCrossAxisAlignment.End => Math.Max(0, free),
        _ => 0,
    };

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

    private static void ValidateAlignment(UiMainAxisAlignment main, UiCrossAxisAlignment cross)
    {
        if (!Enum.IsDefined(main) || !Enum.IsDefined(cross))
        {
            throw new ArgumentOutOfRangeException(nameof(main));
        }
    }
}
