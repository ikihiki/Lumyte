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

    public ValueTask<AssetData> OpenAsync(
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

        try
        {
            Stream content = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ValueTask.FromResult(
                new AssetData(content, new AssetLocation("file", fullPath)));
        }
        catch (FileNotFoundException exception)
        {
            throw new AssetNotFoundException(
                $"The asset file '{fullPath}' was not found.",
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new AssetNotFoundException(
                $"The asset file '{fullPath}' was not found.",
                exception);
        }
        catch (IOException exception)
        {
            throw new AssetSourceException(
                $"The asset file '{fullPath}' could not be opened.",
                exception);
        }
    }
}
