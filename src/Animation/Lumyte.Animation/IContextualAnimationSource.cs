using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Evaluates graph children within one isolated sampling context.</summary>
/// <typeparam name="T">The sampled value type.</typeparam>
internal interface IContextualAnimationSource<T>
{
    /// <summary>Samples using the current call's shared source cache.</summary>
    /// <param name="time">The local sample time.</param>
    /// <param name="context">The current evaluation context.</param>
    /// <returns>The sampled value.</returns>
    T Sample(Duration time, AnimationSourceEvaluationContext context);
}
