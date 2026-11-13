namespace Lumyte.Mathematics.Colors;

/// <summary>Provides the immutable straight-alpha sRGB eight-bit CSS reference catalog.</summary>
public static class CssColors
{
    /// <summary>Gets the immutable 148 named colors plus transparent, keyed by lowercase CSS keyword with ordinal case-insensitive lookup.</summary>
    public static IReadOnlyDictionary<string, SRgba8> All { get; } = NamedColorCatalog.Create();

    /// <summary>Looks up an ASCII named keyword without parsing CSS, trimming whitespace, or resolving contextual colors.</summary>
    /// <param name="name">The ASCII keyword, compared using ordinal case-insensitive equality.</param>
    /// <param name="color">The eight-bit code value, or default for null or an unknown keyword.</param>
    /// <returns>Whether the keyword has a fixed palette value.</returns>
    public static bool TryGet(string? name, out SRgba8 color)
    {
        color = default;
        if (name is null)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (character > 127)
            {
                return false;
            }
        }

        return All.TryGetValue(name, out color);
    }
}
