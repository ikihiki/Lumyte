namespace Lumyte.Animation;

public sealed class LinearCurve : ICurve
{
    public static LinearCurve Instance { get; } = new();

    private LinearCurve()
    {
    }

    public float Transform(float progress) => progress;
}
