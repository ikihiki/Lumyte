namespace Lumyte.UI;

/// <summary>Provides passive placement operations.</summary>
public static partial class UiParentDataFactories
{
    /// <summary>Assigns a finite positive initial weight and finite nonnegative minimum to the original child.</summary>
    /// <param name="factory">The SplitView factory.</param>
    /// <param name="weight">The initial weight, default one.</param>
    /// <param name="minimum">The minimum main-axis extent, default zero.</param>
    /// <returns>A passive operation without an extra widget.</returns>
    public static Action<Compose.Definitions.Widget> Pane(this Compose.Definitions.SplitViewFactory factory, UiArgument<float> weight = default, UiArgument<float> minimum = default) => target =>
    {
        float w = Value(weight, 1);
        float m = Value(minimum, 0);
        UiLayoutMath.ValidateDimension(w, nameof(weight));
        UiLayoutMath.ValidateDimension(m, nameof(minimum));
        ArgumentOutOfRangeException.ThrowIfZero(w);
        target.AddParentData(new(UiParentDataKind.SplitView, Weight: w, Minimum: m));
        Attach(target, weight, w, 6);
        Attach(target, minimum, m, 7);
    };
}
