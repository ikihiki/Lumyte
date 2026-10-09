namespace Lumyte.Animation;

/// <summary>Represents animation state context.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <param name="Input">The input.</param>
/// <param name="Playback">The playback.</param>
public readonly record struct AnimationStateContext<TContext>(TContext Input, AnimationStateInfo Playback);
