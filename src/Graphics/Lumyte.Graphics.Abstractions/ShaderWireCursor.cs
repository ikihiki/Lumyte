namespace Lumyte.Graphics.Abstractions;

/// <summary>Interleaves raw wire ranges and inline reference encoding in one synchronous pass.</summary>
/// <typeparam name="TSink">The destination sink.</typeparam>
/// <param name="source">The complete borrowed C# representation, including padding.</param>
/// <param name="sink">The destination sink, borrowed until completion.</param>
/// <remarks>The cursor borrows both inputs. A failed emission can leave partial private output;
/// the caller must publish only after successful completion. Do not copy an active cursor.</remarks>
public ref struct ShaderWireCursor<TSink>(ReadOnlySpan<byte> source, ref TSink sink) : IShaderReferenceCursor
    where TSink : struct, IShaderWireSink
{
    private readonly ReadOnlySpan<byte> _source = source;
    private readonly ref TSink _sink = ref sink;
    private int _position;
    private bool _completed;

    /// <inheritdoc />
    public void WriteReference<T>(int offset, IGpuRef<T>? value)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The wire cursor is already complete.");
        }

        if (offset < _position || offset > _source.Length - 16)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Reference slots must be ordered, non-overlapping and inside the wire value.");
        }

        if (offset != _position)
        {
            _sink.WriteRaw(_source.Slice(_position, offset - _position), _position);
        }

        _sink.WriteReference(offset, value);
        _position = offset + 16;
    }

    /// <summary>Copies the remaining raw tail and closes the cursor.</summary>
    public void Complete()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The wire cursor is already complete.");
        }

        if (_position != _source.Length)
        {
            _sink.WriteRaw(_source[_position..], _position);
        }

        _position = _source.Length;
        _completed = true;
    }
}
