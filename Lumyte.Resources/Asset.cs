namespace Lumyte.Resources;

/// <summary>Creates canonical typed asset keys.</summary>
public static class Asset
{
    public static AssetKey<T> From<T>(string address) =>
        AssetKey.Create<T>(address, null);

    public static AssetKey<T> From<T>(
        string address,
        IResourceSelector<T> selector) =>
        AssetKey.Create(address, selector);

    public static AssetKey<T> File<T>(string path) =>
        AssetKey.Create<T>($"file:{NormalizeFilePath(path)}", null);

    public static AssetKey<T> File<T>(
        string path,
        IResourceSelector<T> selector) =>
        AssetKey.Create($"file:{NormalizeFilePath(path)}", selector);

    public static AssetKey<T> Id<T>(string id) =>
        AssetKey.Create<T>($"asset:{EscapeIdentifier(id)}", null);

    public static AssetKey<T> Id<T>(
        string id,
        IResourceSelector<T> selector) =>
        AssetKey.Create($"asset:{EscapeIdentifier(id)}", selector);

    private static string EscapeIdentifier(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Uri.EscapeDataString(id);
    }

    private static string NormalizeFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized))
        {
            throw new ArgumentException("Asset file paths must be relative.", nameof(path));
        }

        List<string> segments = [];
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException(
                        "Asset file paths cannot escape their source root.",
                        nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(Uri.EscapeDataString(segment));
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("Asset file paths cannot be empty.", nameof(path));
        }

        return string.Join('/', segments);
    }
}
