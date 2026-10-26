namespace Lumyte.Resources.FileSystem;

internal sealed class FileResourceReadStream : Stream
{
    private readonly FileStream _input;
    private readonly long _offset;
    private readonly long _length;
    private readonly string _path;
    private readonly FileResourceStamp _stamp;

    internal FileResourceReadStream(FileStream input, long offset, long length, string path, FileResourceStamp stamp)
    {
        _input = input;
        _offset = offset;
        _length = length;
        _path = path;
        _stamp = stamp;
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
    {
        _stamp.Verify(_path, _input);
        int requested = (int)Math.Min(buffer.Length, _length - Position);
        int read = _input.Read(buffer[..requested]);
        _stamp.Verify(_path, _input);
        ValidateRead(read, requested);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stamp.Verify(_path, _input);
        int requested = (int)Math.Min(buffer.Length, _length - Position);
        int read = await _input.ReadAsync(buffer[..requested], cancellationToken).ConfigureAwait(false);
        _stamp.Verify(_path, _input);
        ValidateRead(read, requested);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _stamp.Verify(_path, _input);
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (position < 0 || position > _length)
        {
            throw new IOException("The requested position exceeds the input range.");
        }

        _input.Position = checked(_offset + position);
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException("Resource inputs are read-only.");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Resource inputs are read-only.");

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

    private static void ValidateRead(int read, int requested)
    {
        if (read == 0 && requested != 0)
        {
            throw new EndOfStreamException("The resource ended before the observed input range was read.");
        }
    }
}
