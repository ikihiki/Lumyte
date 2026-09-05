namespace Lumyte.Resources;

/// <summary>Identifies a physical location supplied by an asset source.</summary>
public readonly record struct AssetLocation
{
    public AssetLocation(string source, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Source = source;
        Path = path;
    }

    public string Source { get; }

    public string Path { get; }
}
