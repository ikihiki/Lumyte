namespace Lumyte.Animation;

public static class Curves
{
    public static ICurve Linear { get; } = LinearCurve.Instance;

    public static ICurve Ease { get; } = new CubicBezierCurve(0.25f, 0.1f, 0.25f, 1f);

    public static ICurve EaseIn { get; } = new CubicBezierCurve(0.42f, 0f, 1f, 1f);

    public static ICurve EaseOut { get; } = new CubicBezierCurve(0f, 0f, 0.58f, 1f);

    public static ICurve EaseInOut { get; } = new CubicBezierCurve(0.42f, 0f, 0.58f, 1f);

    public static ICurve Step { get; } = new StepsCurve(1);
}
