using System.Reflection;

namespace Lumyte.Resources.Embedded;

internal sealed class EmbeddedResourceReadStream : Stream
{
    private readonly Stream _input;
    private readonly Assembly _assembly;
    private readonly long _offset;
    private readonly long _length;

    internal EmbeddedResourceReadStream(Stream input, Assembly assembly, long offset, long length)
    {
        _input = input;
        _assembly = assembly;
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
    {
        int read = _input.Read(buffer[..(int)Math.Min(buffer.Length, _length - Position)]);
        GC.KeepAlive(_assembly);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _input.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _length - Position)], cancellationToken).ConfigureAwait(false);
        GC.KeepAlive(_assembly);
        return read;
    }

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
        GC.KeepAlive(_assembly);
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException("Embedded resources are read-only.");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Embedded resources are read-only.");

    public override async ValueTask DisposeAsync()
    {
        await _input.DisposeAsync().ConfigureAwait(false);
        GC.KeepAlive(_assembly);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
            GC.KeepAlive(_assembly);
        }

        base.Dispose(disposing);
    }
}
