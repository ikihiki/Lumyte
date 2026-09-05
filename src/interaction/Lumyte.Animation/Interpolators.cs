using System.Numerics;

namespace Lumyte.Animation;

public static class Interpolators
{
    public static IInterpolator<float> Float { get; } =
        new DelegateInterpolator<float>((from, to, progress) => from + ((to - from) * progress));

    public static IInterpolator<double> Double { get; } =
        new DelegateInterpolator<double>((from, to, progress) => from + ((to - from) * progress));

    public static IInterpolator<Vector2> Vector2 { get; } =
        new DelegateInterpolator<Vector2>(System.Numerics.Vector2.Lerp);

    public static IInterpolator<Vector3> Vector3 { get; } =
        new DelegateInterpolator<Vector3>(System.Numerics.Vector3.Lerp);

    public static IInterpolator<Vector4> Vector4 { get; } =
        new DelegateInterpolator<Vector4>(System.Numerics.Vector4.Lerp);

    public static IInterpolator<Quaternion> Quaternion { get; } =
        new DelegateInterpolator<Quaternion>(System.Numerics.Quaternion.Slerp);

    public static IInterpolator<T> Discrete<T>() => DiscreteInterpolator<T>.Instance;

    public static IInterpolator<T> Create<T>(Func<T, T, float, T> interpolate)
    {
        ArgumentNullException.ThrowIfNull(interpolate);
        return new DelegateInterpolator<T>(interpolate);
    }
}
