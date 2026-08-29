namespace Lumyte.Animation;

public abstract record AnimationChannel
{
    protected AnimationChannel(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public abstract Type ValueType { get; }
}
