namespace Lumyte.Animation;

internal sealed class AnimationValueSlot<T> : IAnimationValueSlot
{
    public T Value { get; set; } = default!;
}
