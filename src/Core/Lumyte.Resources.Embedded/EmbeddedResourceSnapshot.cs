using System.Reflection;

namespace Lumyte.Resources.Embedded;

internal sealed class EmbeddedResourceSnapshot(Assembly assembly, ResourceId id, string name, string revision, long totalLength) : ResourceSourceSnapshot
{
    private readonly object _gate = new();
    private bool _disposed;

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
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stream input = EmbeddedResourceSource.OpenInput(assembly, name);
            try
            {
                Stream range = new EmbeddedResourceReadStream(input, assembly, offset, length ?? totalLength - offset);
                return ValueTask.FromResult(range);
            }
            catch
            {
                input.Dispose();
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
