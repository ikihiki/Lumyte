namespace Lumyte.Animation;

public interface IInterpolator<T>
{
    T Interpolate(T from, T to, float progress);
}
