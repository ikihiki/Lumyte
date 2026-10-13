namespace Lumyte.Resources.FileSystem;

internal sealed class FileResourceSnapshot(ResourceId id, string revision, long totalLength, string path, FileStream storage) : ResourceSourceSnapshot
{
    private readonly object _gate = new();
    private Task? _disposal;

    public override ResourceId Id => id;

    public override string Revision => revision;

    public override long? Length => totalLength;

    public override bool CanReadRange => true;

    public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (length is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (offset > totalLength || length > totalLength - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The requested range exceeds the snapshot.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
            try
            {
                Stream range = new FileResourceReadStream(reader, offset, length ?? totalLength - offset);
                return ValueTask.FromResult(range);
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }
    }

    public override ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposal ??= storage.DisposeAsync().AsTask();
            return new(_disposal);
        }
    }
}
