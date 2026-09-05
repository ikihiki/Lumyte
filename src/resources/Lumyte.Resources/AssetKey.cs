namespace Lumyte.Resources;

/// <summary>Identifies a typed resource using canonical text and its component positions.</summary>
public readonly record struct AssetKey<T>
{
    private readonly string text;
    private readonly int addressStart;
    private readonly int selectorStart;

    internal AssetKey(string text, int addressStart, int selectorStart)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.text = text;
        this.addressStart = addressStart;
        this.selectorStart = selectorStart;
    }

    public bool IsValid => text is not null;

    public ReadOnlySpan<char> Text => text.AsSpan();

    public ReadOnlySpan<char> Scheme =>
        text.AsSpan(0, Math.Max(0, addressStart - 1));

    public ReadOnlySpan<char> Address
    {
        get
        {
            int addressEnd = selectorStart == text?.Length
                ? selectorStart
                : selectorStart - 1;
            return text.AsSpan(addressStart, addressEnd - addressStart);
        }
    }

    public ReadOnlySpan<char> Selector =>
        text.AsSpan(selectorStart, (text?.Length ?? 0) - selectorStart);

    internal string CanonicalText => text
        ?? throw new InvalidOperationException("The asset key is not initialized.");

    internal int AddressStart => addressStart;

    internal int SelectorStart => selectorStart;

    public static AssetKey<T> Parse(string text) =>
        AssetKey.Parse<T>(text);

    public override string ToString() =>
        $"AssetKey<{typeof(T).Name}>({text ?? string.Empty})";
}

internal static class AssetKey
{
    internal static AssetKey<T> Create<T>(string address, IResourceSelector<T>? selector)
    {
        string normalizedAddress = NormalizeAddress(address);
        if (selector is null)
        {
            return Parse<T>(normalizedAddress);
        }

        ResourceSelectorBuilder selectorBuilder = new();
        selector.WriteTo(selectorBuilder);
        if (selectorBuilder.IsEmpty)
        {
            throw new ArgumentException(
                "Resource selectors must contain at least one segment.",
                nameof(selector));
        }

        return Parse<T>($"{normalizedAddress}#{selectorBuilder.Build()}");
    }

    internal static AssetKey<T> Parse<T>(string text)
    {
        string normalizedText = Normalize(text);
        (int schemeEnd, int separatorStart) = GetSeparators(normalizedText);
        int addressStart = schemeEnd + 1;
        int selectorStart = separatorStart < 0
            ? normalizedText.Length
            : separatorStart + 1;
        return new AssetKey<T>(normalizedText, addressStart, selectorStart);
    }

    internal static string NormalizeAddress(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (address.Contains('#'))
        {
            throw new FormatException("Asset addresses cannot contain a fragment.");
        }

        return Normalize(address);
    }

    internal static string Normalize(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        int schemeEnd = text.IndexOf(':');
        if (schemeEnd <= 0)
        {
            throw new FormatException("Asset keys require a URI scheme.");
        }

        int selectorStart = text.IndexOf('#', schemeEnd + 1);
        int addressEnd = selectorStart < 0 ? text.Length : selectorStart;
        if (addressEnd == schemeEnd + 1)
        {
            throw new FormatException("Asset keys require an address.");
        }

        string normalizedScheme = text[..schemeEnd].ToLowerInvariant();
        return normalizedScheme == text[..schemeEnd]
            ? text
            : string.Concat(normalizedScheme, text.AsSpan(schemeEnd));
    }

    internal static (int SchemeEnd, int SelectorStart) GetSeparators(string text)
    {
        int schemeEnd = text.IndexOf(':');
        int selectorStart = text.IndexOf('#', schemeEnd + 1);
        return (schemeEnd, selectorStart);
    }

    internal static string NormalizeScheme(string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        return scheme.ToLowerInvariant();
    }

}
