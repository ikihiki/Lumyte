namespace Lumyte.Animation;

public sealed class AnimationBlend
{
    private readonly Dictionary<AnimationChannel, IBlendOperation> operations = [];

    public AnimationBlend Use<T>(AnimationChannel<T> channel, IInterpolator<T> interpolator)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(interpolator);
        operations[channel] = new BlendOperation<T>(interpolator);
        return this;
    }

    internal object? Interpolate(AnimationChannel channel, object? from, object? to, float progress)
    {
        if (!operations.TryGetValue(channel, out IBlendOperation? operation))
        {
            throw new InvalidOperationException($"Animation channel '{channel.Name}' has no blend operation.");
        }

        return operation.Interpolate(from, to, progress);
    }

    internal void InterpolateInto(
        AnimationChannel channel,
        AnimationSample from,
        AnimationSampleBuffer destination,
        float progress)
    {
        if (!operations.TryGetValue(channel, out IBlendOperation? operation))
        {
            throw new InvalidOperationException($"Animation channel '{channel.Name}' has no blend operation.");
        }

        operation.InterpolateInto(channel, from, destination, progress);
    }

    private interface IBlendOperation
    {
        object? Interpolate(object? from, object? to, float progress);

        void InterpolateInto(
            AnimationChannel channel,
            AnimationSample from,
            AnimationSampleBuffer destination,
            float progress);
    }

    private sealed class BlendOperation<T>(IInterpolator<T> interpolator) : IBlendOperation
    {
        public object? Interpolate(object? from, object? to, float progress) =>
            interpolator.Interpolate((T)from!, (T)to!, progress);

        public void InterpolateInto(
            AnimationChannel channel,
            AnimationSample from,
            AnimationSampleBuffer destination,
            float progress)
        {
            var typedChannel = (AnimationChannel<T>)channel;
            destination.Set(
                typedChannel,
                interpolator.Interpolate(
                    from.Get(typedChannel),
                    destination.Get(typedChannel),
                    progress));
        }
    }
}
