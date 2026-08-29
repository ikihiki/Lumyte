namespace Lumyte.Composition;

/// <summary>Distinguishes an omitted factory argument from an explicitly supplied default or null value.</summary>
public readonly struct Optional<T>
{
    private readonly T _value;

    public bool HasValue { get; }

    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException("The optional value was not supplied.");

    public Optional(T value)
    {
        _value = value;
        HasValue = true;
    }

    public static implicit operator Optional<T>(T value) => new(value);
}
