namespace Lumyte.Animation;

/// <summary>An opaque typed result identifier without a target reference or setter.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class AnimationChannel<T>
{
    private AnimationChannel()
    {
    }

    /// <summary>Creates a unique typed result identifier without a target binding.</summary>
    /// <returns>The computed result.</returns>
    public static AnimationChannel<T> Create() => new();
}
