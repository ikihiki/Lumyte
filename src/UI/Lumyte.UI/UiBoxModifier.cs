using System.Numerics;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

internal enum UiBoxModifierKind
{
    Padding,
    Border,
    Align,
    Constraints,
}

internal readonly record struct UiBoxModifier(UiBoxModifierKind Kind, float Left = 0, float Top = 0, float Right = 0, float Bottom = 0, Rgba<float> Color = default, UiBoxConstraints Constraints = default)
{
    public Rgba<float> Color { get; init => field = UiColorPolicy.Unit(value, nameof(Color)); } = UiColorPolicy.Unit(Color, nameof(Color));

    internal UiBoxConstraints Forward(UiBoxConstraints parent)
    {
        if (Kind == UiBoxModifierKind.Align)
        {
            return parent.Loosen();
        }

        if (Kind == UiBoxModifierKind.Constraints)
        {
            return new(Math.Clamp(Constraints.MinWidth, parent.MinWidth, parent.MaxWidth), Math.Clamp(Constraints.MaxWidth, parent.MinWidth, parent.MaxWidth), Math.Clamp(Constraints.MinHeight, parent.MinHeight, parent.MaxHeight), Math.Clamp(Constraints.MaxHeight, parent.MinHeight, parent.MaxHeight));
        }

        float horizontal = UiLayoutMath.FiniteFloat((double)Left + Right);
        float vertical = UiLayoutMath.FiniteFloat((double)Top + Bottom);
        return new(Math.Max(0, parent.MinWidth - horizontal), Math.Max(0, parent.MaxWidth - horizontal), Math.Max(0, parent.MinHeight - vertical), Math.Max(0, parent.MaxHeight - vertical));
    }

    internal Vector2 Backward(UiBoxConstraints parent, Vector2 inner, out Vector2 offset)
    {
        if (Kind == UiBoxModifierKind.Align)
        {
            Vector2 size = parent.Constrain(new(parent.HasBoundedWidth ? parent.MaxWidth : inner.X, parent.HasBoundedHeight ? parent.MaxHeight : inner.Y));
            offset = new((size.X - inner.X) * Left, (size.Y - inner.Y) * Top);
            return size;
        }

        if (Kind == UiBoxModifierKind.Constraints)
        {
            offset = Vector2.Zero;
            return inner;
        }

        offset = new(Left, Top);
        return parent.Constrain(new(UiLayoutMath.FiniteFloat((double)inner.X + Left + Right), UiLayoutMath.FiniteFloat((double)inner.Y + Top + Bottom)));
    }
}
