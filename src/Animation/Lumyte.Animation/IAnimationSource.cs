using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Provides immutable, side-effect-free value sampling at typed local times.</summary>
/// <typeparam name="T">The value type.</typeparam>
public interface IAnimationSource<T>
{
    /// <summary>Gets the duration.</summary>
    Duration Duration { get; }

    /// <summary>Computes a value at a local time within the source duration without side effects.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The computed result.</returns>
    T Sample(Duration time);
}
