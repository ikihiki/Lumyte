namespace Lumyte.Animation;

public sealed class DiscreteInterpolator<T> : IInterpolator<T>
{
    public static DiscreteInterpolator<T> Instance { get; } = new();

    private DiscreteInterpolator()
    {
    }

    public T Interpolate(T from, T to, float progress) => progress < 1f ? from : to;
}
