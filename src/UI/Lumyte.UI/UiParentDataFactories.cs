namespace Lumyte.UI;

/// <summary>Provides passive parent placement operations on existing declarations.</summary>
public static class UiParentDataFactories
{
    /// <summary>Places the same widget in an explicit grid cell.</summary>
    /// <param name="factory">The Grid factory.</param>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <param name="rowSpan">The number of rows.</param>
    /// <param name="columnSpan">The number of columns.</param>
    /// <returns>An operation on the original declaration.</returns>
    public static Action<Compose.Definitions.Widget> Cell(this Compose.Definitions.GridFactory factory, UiArgument<int> row = default, UiArgument<int> column = default, UiArgument<int> rowSpan = default, UiArgument<int> columnSpan = default) => target =>
    {
        int r = Value(row, 0);
        int c = Value(column, 0);
        int rs = Value(rowSpan, 1);
        int cs = Value(columnSpan, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(r);
        ArgumentOutOfRangeException.ThrowIfNegative(c);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cs);
        target.AddParentData(new(UiParentDataKind.Grid, r, c, rs, cs));
        Attach(target, row, r, 0);
        Attach(target, column, c, 1);
        Attach(target, rowSpan, rs, 2);
        Attach(target, columnSpan, cs, 3);
    };

    /// <summary>Assigns a weighted main-axis share with tight or loose constraints.</summary>
    /// <param name="factory">The Flex factory.</param>
    /// <param name="weight">The positive finite weight.</param>
    /// <param name="fit">Whether to fill the assigned share.</param>
    /// <returns>An operation on the original declaration.</returns>
    public static Action<Compose.Definitions.Widget> Flexible(this Compose.Definitions.FlexFactory factory, UiArgument<float> weight = default, UiArgument<UiFlexFit> fit = default) => target =>
    {
        float w = Value(weight, 1);
        UiFlexFit f = Value(fit, UiFlexFit.Loose);
        UiLayoutMath.ValidateDimension(w, nameof(weight));
        ArgumentOutOfRangeException.ThrowIfZero(w);
        if (!Enum.IsDefined(f))
        {
            throw new ArgumentOutOfRangeException(nameof(fit));
        }

        target.AddParentData(new(UiParentDataKind.Flex, Weight: w, Fit: f));
        Attach(target, weight, w, 4);
        Attach(target, fit, f, 5);
    };

    /// <summary>Fills a weighted main-axis share.</summary>
    /// <param name="factory">The Flex factory.</param>
    /// <param name="weight">The positive finite weight.</param>
    /// <returns>An operation on the original declaration.</returns>
    public static Action<Compose.Definitions.Widget> Expanded(this Compose.Definitions.FlexFactory factory, UiArgument<float> weight = default) => factory.Flexible(weight, UiFlexFit.Tight);

    private static T Value<T>(UiArgument<T> argument, T fallback) => argument.IsSpecified ? argument.GetInitialValue() : fallback;

    private static void Attach<T>(Compose.Definitions.Widget target, UiArgument<T> argument, T initial, int member)
    {
        if (argument.Source is { } source)
        {
            object key = (typeof(UiParentDataFactories), target.ParentData.Length, member);
            target.AddSignalArgument(key, source, () => !EqualityComparer<T>.Default.Equals(source.Value, initial));
            if (source is IUiAnimationProjection projection)
            {
                target.AddAnimationArgument(key, projection, () => !EqualityComparer<T>.Default.Equals(source.Peek(), initial));
            }
        }
    }
}
