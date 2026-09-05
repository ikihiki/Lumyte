namespace Lumyte.Resources;

/// <summary>Owns the readable data opened for one asset.</summary>
public sealed class AssetData : IAsyncDisposable
{
    public AssetData(Stream content, AssetLocation location)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("Asset data streams must be readable.", nameof(content));
        }

        Content = content;
        Location = location;
    }

    public Stream Content { get; }

    public AssetLocation Location { get; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
