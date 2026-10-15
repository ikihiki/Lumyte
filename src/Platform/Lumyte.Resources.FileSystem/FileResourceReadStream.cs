namespace Lumyte.Resources.FileSystem;

internal sealed class FileResourceReadStream : Stream
{
    private readonly FileStream _input;
    private readonly long _offset;
    private readonly long _length;

    internal FileResourceReadStream(FileStream input, long offset, long length)
    {
        _input = input;
        _offset = offset;
        _length = length;
        _input.Position = offset;
    }

    public override bool CanRead => _input.CanRead;

    public override bool CanSeek => _input.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _input.Position - _offset;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
        => _input.Read(buffer[..(int)Math.Min(buffer.Length, _length - Position)]);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _input.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _length - Position)], cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (position < 0 || position > _length)
        {
            throw new IOException("The requested position exceeds the snapshot range.");
        }

        _input.Position = checked(_offset + position);
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException("Resource snapshots are read-only.");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Resource snapshots are read-only.");

    public override async ValueTask DisposeAsync()
    {
        await _input.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
