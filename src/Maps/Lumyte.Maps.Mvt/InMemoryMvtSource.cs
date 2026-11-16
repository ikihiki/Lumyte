using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Portable source borrowing an immutable registration; snapshots share its private bounded storage.</summary>
/// <param name="registration">Registration also supplied to the decoder stage.</param>
public sealed class InMemoryMvtSource(InMemoryMvtRegistration registration) : IResourceSource
{
    /// <inheritdoc />
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InMemoryMvtRegistration.Entry entry = registration.Resolve(id);
        return ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(registration, id, entry));
    }

    /// <inheritdoc />
    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ValueTask.FromResult<string?>(registration.Resolve(id).Revision);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<string?>(null);
        }
    }

    internal sealed class Snapshot(InMemoryMvtRegistration registration, ResourceId id, InMemoryMvtRegistration.Entry entry) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => entry.Revision;

        public override long? Length => entry.Bytes.Length;

        public override bool CanReadRange => false;

        internal InMemoryMvtRegistration Registration => registration;

        internal ReadOnlyMemory<byte> Content => entry.Bytes;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset != 0 || length is not null)
            {
                throw new NotSupportedException("This bounded snapshot supports complete reads only.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(entry.Bytes, false));
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
