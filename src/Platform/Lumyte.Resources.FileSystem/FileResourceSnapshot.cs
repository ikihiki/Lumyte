namespace Lumyte.Resources.FileSystem;

internal sealed class FileResourceSnapshot(ResourceId id, FileResourceStamp stamp, string path) : ResourceSourceSnapshot
{
    private readonly object _gate = new();
    private bool _disposed;

    public override ResourceId Id => id;

    public override string Revision => stamp.Revision;

    public override long? Length => stamp.Length;

    public override bool CanReadRange => true;

    public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (length is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (offset > stamp.Length || length > stamp.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The requested range exceeds the observed input.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FileStream reader = FileResourceSource.OpenInput(path);
            try
            {
                stamp.Verify(path, reader);
                Stream range = new FileResourceReadStream(reader, offset, length ?? stamp.Length - offset, path, stamp);
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
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
