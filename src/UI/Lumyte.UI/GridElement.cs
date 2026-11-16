using System.Numerics;
using static Lumyte.UI.Compose.Definitions;

namespace Lumyte.UI;

internal sealed class GridElement(Grid widget) : UiElement<Grid>(widget)
{
    protected override UiInvalidation CompareConfiguration(Grid previous, Grid next) => previous.Rows.SequenceEqual(next.Rows) && previous.Columns.SequenceEqual(next.Columns) && previous.RowSpacing == next.RowSpacing && previous.ColumnSpacing == next.ColumnSpacing ? UiInvalidation.None : UiInvalidation.Layout;

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        double[] rows = Resolve(Widget.Rows, Widget.RowSpacing, constraints.MaxHeight);
        double[] columns = Resolve(Widget.Columns, Widget.ColumnSpacing, constraints.MaxWidth);
        double[] rowStarts = Starts(rows, Widget.RowSpacing);
        double[] columnStarts = Starts(columns, Widget.ColumnSpacing);
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            var cell = (GridCell)child.Widget;
            double x = columnStarts[cell.Column];
            double y = rowStarts[cell.Row];
            double width = Length(columns, Widget.ColumnSpacing, cell.Column, cell.ColumnSpan);
            double height = Length(rows, Widget.RowSpacing, cell.Row, cell.RowSpan);
            _ = LayoutChild(child, UiBoxConstraints.Tight(new(UiLayoutMath.FiniteFloat(width), UiLayoutMath.FiniteFloat(height))));
            SetChildOffset(child, new(UiLayoutMath.FiniteFloat(x), UiLayoutMath.FiniteFloat(y)));
        }

        return UiLayoutMath.FillBounded(constraints, Length(columns, Widget.ColumnSpacing, 0, columns.Length), Length(rows, Widget.RowSpacing, 0, rows.Length));
    }

    private static double[] Resolve(IReadOnlyList<UiGridTrack> tracks, float gap, float maximum)
    {
        double fixedSize = (double)Math.Max(0, tracks.Count - 1) * gap;
        double weight = 0;
        foreach (UiGridTrack track in tracks)
        {
            fixedSize += track.IsRatio ? 0 : track.Value;
            weight += track.IsRatio ? track.Value : 0;
        }

        if (weight > 0 && !float.IsFinite(maximum))
        {
            throw new InvalidOperationException("Ratio tracks require a bounded axis.");
        }

        double remaining = float.IsFinite(maximum) ? Math.Max(0, maximum - fixedSize) : 0;
        double[] sizes = new double[tracks.Count];
        for (int index = 0; index < tracks.Count; index++)
        {
            UiGridTrack track = tracks[index];
            sizes[index] = track.IsRatio ? remaining * (track.Value / weight) : track.Value;
        }

        return sizes;
    }

    private static double[] Starts(double[] sizes, float gap)
    {
        double[] starts = new double[sizes.Length + 1];
        for (int index = 0; index < sizes.Length; index++)
        {
            starts[index + 1] = starts[index] + sizes[index] + gap;
        }

        return starts;
    }

    private static double Length(double[] sizes, float gap, int index, int count)
    {
        double result = (double)Math.Max(0, count - 1) * gap;
        for (int current = index; current < index + count; current++)
        {
            result += sizes[current];
        }

        return result;
    }
}
