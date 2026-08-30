namespace Lumyte.Resources;

/// <summary>Provides one fixed dependency generation while a resource is being built.</summary>
public readonly record struct ResourceDependency<T>
    where T : notnull
{
    internal ResourceDependency(ResourceId<T> id, T value, uint generation)
    {
        Id = id;
        Value = value;
        Generation = generation;
    }

    public ResourceId<T> Id { get; }

    public T Value { get; }

    public uint Generation { get; }
}
