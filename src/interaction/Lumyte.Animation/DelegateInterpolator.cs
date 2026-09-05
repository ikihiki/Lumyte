namespace Lumyte.Animation;

internal sealed class DelegateInterpolator<T>(Func<T, T, float, T> interpolate) : IInterpolator<T>
{
    public T Interpolate(T from, T to, float progress) => interpolate(from, to, progress);
}
