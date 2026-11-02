namespace Lumyte.UI;

/// <summary>An ordinal visual identity confined to one navigation slot.</summary>
public readonly record struct UiSharedElementKey
{
    /// <summary>Initializes a new instance of the <see cref="UiSharedElementKey"/> struct.</summary>
    /// <param name="value">A nonempty identifier of at most 128 noncontrol characters.</param>
    public UiSharedElementKey(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Use 1 through 128 noncontrol characters.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the ordinal identifier; a default key is invalid.</summary>
    public string Value { get; }
}
