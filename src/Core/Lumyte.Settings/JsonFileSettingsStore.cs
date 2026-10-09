namespace Lumyte.Settings;

/// <summary>Persists settings through same-directory atomic file replacement.</summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _path;

    /// <summary>Initializes a new instance of the <see cref="JsonFileSettingsStore"/> class.</summary>
    /// <param name="path">The absolute settings file path.</param>
    public JsonFileSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute settings path is required.", nameof(path));
        }

        _path = path;
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException("Cannot read the settings file.", error);
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)!;
        string temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException("Cannot replace the settings file.", error);
        }
        finally
        {
            // Cleanup must not convert a committed write into an apparent failure.
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal byte[]? Read()
    {
        try
        {
            return File.ReadAllBytes(_path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException("Cannot read the settings file.", error);
        }
    }
}
