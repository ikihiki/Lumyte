namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Immutable finite file source; each open allocates a fresh stream.</summary>
/// <param name="bytes">Complete WAVE file.</param>
/// <param name="partialReads">Use nonseekable seven-byte reads and unknown length.</param>
public sealed class WaveSource(byte[] bytes, bool partialReads) : IResourceSource
{
    private readonly byte[] _bytes = bytes;
    private readonly bool _partialReads = partialReads;
    private int _opens;

    /// <summary>Gets actual stream open count.</summary>
    public int Opens => _opens;

    /// <inheritdoc/>
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, this));
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>("synthetic-v1");
    }

    private sealed class Snapshot(ResourceId id, WaveSource source) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "synthetic-v1";

        public override long? Length => source._partialReads ? null : source._bytes.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset != 0 || length is not null)
            {
                throw new NotSupportedException("Only complete-file reads are supported.");
            }

            source._opens++;
            return ValueTask.FromResult<Stream>(source._partialReads ? new PartialStream(source._bytes) : new MemoryStream(source._bytes, writable: false));
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PartialStream(byte[] content) : Stream
    {
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(Math.Min(buffer.Length, 7), content.Length - _offset);
            content.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
