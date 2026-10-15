namespace Lumyte.Memory;

/// <summary>Identifies one independently budgeted memory pool.</summary>
public readonly record struct MemoryPoolId
{
    /// <summary>Initializes a new instance of the <see cref="MemoryPoolId"/> struct.</summary>
    /// <param name="value">The nonempty ordinal identifier.</param>
    public MemoryPoolId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the identifier, or <see langword="null"/> for an uninitialized value.</summary>
    public string? Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
