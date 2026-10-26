namespace Lumyte.Resources;

/// <summary>Identifies a resource by an ordinal, source-relative logical path.</summary>
public readonly record struct ResourceId
{
    /// <summary>Initializes a new instance of the <see cref="ResourceId"/> struct.</summary>
    /// <param name="value">A nonempty slash-separated path without traversal or URI syntax.</param>
    public ResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        foreach (char character in value)
        {
            if (char.IsControl(character) || character is '\\' or ':' or '?' or '#')
            {
                throw new ArgumentException("Resource identifiers must be relative logical paths without URI syntax or control characters.", nameof(value));
            }
        }

        foreach (string segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException("Resource identifiers cannot contain empty or traversal segments.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>Gets the logical path, or null for an uninitialized identifier.</summary>
    public string? Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
