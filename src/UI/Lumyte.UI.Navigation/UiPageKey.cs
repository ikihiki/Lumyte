namespace Lumyte.UI.Navigation;

/// <summary>Identifies a static route and its exact argument type.</summary>
/// <typeparam name="TArgs">The registered argument type.</typeparam>
public sealed class UiPageKey<TArgs>
{
    /// <summary>Initializes a new instance of the <see cref="UiPageKey{TArgs}"/> class.</summary>
    /// <param name="value">An absolute lowercase ASCII route, at most 256 characters.</param>
    public UiPageKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 256 || value[0] != '/' || (value.Length > 1 && value.Split('/').Skip(1).Any(segment => segment.Length == 0 || segment[0] is < 'a' or > 'z' || segment.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_' and not '.'))))
        {
            throw new ArgumentException("Use / or lowercase ASCII route segments beginning with a letter.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the canonical ordinal route.</summary>
    public string Value { get; }
}
