namespace Lumyte.UI;

internal enum UiParentDataKind
{
    Grid,
    Flex,
    SplitView,
}

internal readonly record struct UiParentData(UiParentDataKind Kind, int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1, float Weight = 1, UiFlexFit Fit = UiFlexFit.Tight, float Minimum = 0);
