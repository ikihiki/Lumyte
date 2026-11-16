using System.Security.Cryptography;
using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Local-only source with bounded allocation and content-hash revisions.</summary>
/// <param name="registration">Registration also used by the stage.</param>
public sealed class LocalMvtSource(LocalMvtRegistration registration) : IResourceSource
{
    /// <inheritdoc />
    public async ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        string path = registration.Resolve(id);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous);
        long length = stream.Length;
        MapTileLoadProfile profile = registration.Profile;
        if (length > profile.MaxInputBytes || ((length + 31) & ~7L) + 512 > profile.MaxReservationBytesPerTile || length > int.MaxValue)
        {
            throw new InvalidDataException("Local input capacity exceeded.");
        }

        byte[] bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (stream.Length != length)
        {
            throw new IOException("Local input changed while reading.");
        }

        string revision = Convert.ToHexString(SHA256.HashData(bytes));
        return new Snapshot(registration, id, bytes, revision);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using ResourceSourceSnapshot snapshot = await OpenSnapshotAsync(id, cancellationToken);
            return snapshot.Revision;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal sealed class Snapshot(LocalMvtRegistration registration, ResourceId id, byte[] bytes, string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => bytes.Length;

        public override bool CanReadRange => false;

        internal LocalMvtRegistration Registration => registration;

        internal ReadOnlyMemory<byte> Content => bytes;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset != 0 || length is not null)
            {
                throw new NotSupportedException("This bounded snapshot supports complete reads only.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, false));
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
