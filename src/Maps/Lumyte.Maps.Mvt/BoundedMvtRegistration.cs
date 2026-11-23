using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Transport-neutral identity and admission boundary for bounded, immutable MVT snapshots.</summary>
public sealed class BoundedMvtRegistration
{
    private readonly LocalMvtRegistration _identity;
    private readonly HashSet<ResourceId> _ids;

    /// <summary>Initializes a new instance of the <see cref="BoundedMvtRegistration"/> class with explicit finite identities.</summary>
    /// <param name="dataSetId">Dataset identity.</param>
    /// <param name="contentVersion">Immutable representation policy identity.</param>
    /// <param name="profile">Decoder and input limits.</param>
    /// <param name="tiles">Explicit supported tile IDs.</param>
    public BoundedMvtRegistration(string dataSetId, string contentVersion, MapTileLoadProfile profile, IEnumerable<MapTileId> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _identity = new(dataSetId, contentVersion, profile, new Dictionary<MapTileId, string>());
        _ids = [];
        foreach (MapTileId tile in tiles)
        {
            if (_ids.Count >= 4096)
            {
                throw new ArgumentException("Registration metadata capacity exceeded.", nameof(tiles));
            }

            if (!_ids.Add(GetKey(tile).Address.Id))
            {
                throw new ArgumentException("Duplicate normalized tile identity.", nameof(tiles));
            }
        }
    }

    /// <summary>Gets the exact shared decoder profile.</summary>
    public MapTileLoadProfile Profile => _identity.Profile;

    /// <summary>Creates a normalized CPU key.</summary>
    /// <param name="tile">XYZ identity.</param>
    /// <returns>The complete resource key.</returns>
    public ResourceKey<MapTileContent> GetKey(MapTileId tile) => _identity.GetKey(tile);

    /// <summary>Checks that an ID is explicitly registered.</summary>
    /// <param name="id">Resource identity.</param>
    public void Validate(ResourceId id)
    {
        if (!_ids.Contains(id))
        {
            throw new FileNotFoundException("Tile is not registered.");
        }
    }

    /// <summary>Admits immutable transport-owned bytes without copying. The owner must never mutate or expose writable storage.</summary>
    /// <param name="id">Registered identity.</param>
    /// <param name="bytes">Immutable private storage, kept alive by the snapshot.</param>
    /// <param name="revision">Content revision of these exact bytes.</param>
    /// <param name="release">Optional accounting callback when the snapshot is disposed.</param>
    /// <returns>A bounded snapshot accepted only by this registration's stage.</returns>
    public ResourceSourceSnapshot CreateSnapshot(ResourceId id, ReadOnlyMemory<byte> bytes, string revision, Action? release = null)
    {
        Validate(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        if (bytes.Length > Profile.MaxInputBytes || ((bytes.Length + 31L) & ~7L) + 512 > Profile.MaxReservationBytesPerTile)
        {
            throw new InvalidDataException("Transport input capacity exceeded.");
        }

        return new Snapshot(this, id, bytes, revision, release);
    }

    internal void Validate(ResourceAddress address)
    {
        Validate(address.Id);
        if (address.Variant != _identity.Variant || address.Subresource.Length != 0)
        {
            throw new InvalidOperationException("Tile address does not match its registered profile and decoder.");
        }
    }

    internal sealed class Snapshot(BoundedMvtRegistration registration, ResourceId id, ReadOnlyMemory<byte> content, string revision, Action? release) : ResourceSourceSnapshot
    {
        private readonly object _sync = new();
        private readonly int _length = content.Length;
        private ReadOnlyMemory<byte> _content = content;
        private Action? _release = release;
        private int _owners = 1;
        private bool _disposed;

        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => _length;

        public override bool CanReadRange => false;

        internal BoundedMvtRegistration Registration => registration;

        internal ReadOnlyMemory<byte> Content
        {
            get
            {
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return _content;
                }
            }
        }

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset != 0 || length is not null)
            {
                throw new NotSupportedException("Complete reads only.");
            }

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _owners++;
                return ValueTask.FromResult<Stream>(new ReadOnlyMemoryStream(_content, Release));
            }
        }

        public override ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _content = default;
                    Release();
                }
            }

            return ValueTask.CompletedTask;
        }

        private void Release()
        {
            lock (_sync)
            {
                if (--_owners == 0)
                {
                    Interlocked.Exchange(ref _release, null)?.Invoke();
                }
            }
        }
    }

    private sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> bytes, Action release) : Stream
    {
        private ReadOnlyMemory<byte> _bytes = bytes;
        private Action? _release = release;
        private int _position;

        public override bool CanRead => _release is not null;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_release is null, this);
            int count = Math.Min(buffer.Length, _bytes.Length - _position);
            _bytes.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _bytes = default;
            Interlocked.Exchange(ref _release, null)?.Invoke();
            base.Dispose(disposing);
        }
    }
}
