namespace Lumyte.Animation;

/// <summary>Interpolates two typed values using progress that may overshoot when temporal easing requests it.</summary>
/// <typeparam name="T">The value type.</typeparam>
public interface IAnimationInterpolator<T>
{
    /// <summary>Validates an input before an animation source stores it; the default accepts any value.</summary>
    /// <param name="value">The endpoint or keyframe value.</param>
    void Validate(T value)
    {
    }

    /// <summary>Interpolates two values using the supplied progress.</summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="amount">The progress; custom temporal easing may supply values outside [0, 1].</param>
    /// <returns>The computed result.</returns>
    T Interpolate(T from, T to, float amount);
}
