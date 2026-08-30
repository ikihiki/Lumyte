namespace Lumyte.Resources;

/// <summary>Resolves development file addresses below one configured source root.</summary>
public sealed class FileAssetResolver : IAssetResolver
{
    private readonly string root;

    public FileAssetResolver(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
    }

    public string Scheme => "file";

    public ValueTask<AssetLocation> ResolveAsync(
        AssetAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string relativePath = Uri.UnescapeDataString(address.ToString())
            .Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string relativeToRoot = Path.GetRelativePath(root, fullPath);

        if (relativeToRoot == ".."
            || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativeToRoot))
        {
            throw new AssetResolutionException(
                "The asset file address escapes its configured source root.");
        }

        return ValueTask.FromResult(new AssetLocation("file", fullPath));
    }
}
