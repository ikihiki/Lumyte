namespace Lumyte.Animation;

public sealed record AnimationChannel<T> : AnimationChannel
{
    public AnimationChannel(string name)
        : base(name)
    {
    }

    public override Type ValueType => typeof(T);
}
