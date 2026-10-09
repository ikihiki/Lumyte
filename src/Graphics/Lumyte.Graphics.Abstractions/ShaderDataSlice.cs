namespace Lumyte.Graphics.Abstractions;

/// <summary>References a nonempty shader data range; default is invalid.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public readonly struct ShaderDataSlice<T>
    where T : struct, IShaderData
{
    /// <summary>Initializes a new instance of the <see cref="ShaderDataSlice{T}"/> struct.</summary>
    /// <param name="buffer">The logical allocation.</param>
    /// <param name="offset">The first logical element.</param>
    /// <param name="count">The element count.</param>
    public ShaderDataSlice(IGraphicsShaderDataBuffer<T> buffer, ulong offset, ulong count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count == 0 || offset > buffer.Count || count > buffer.Count - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        (Buffer, Offset, Count) = (buffer, offset, count);
    }

    /// <summary>Gets the logical allocation.</summary>
    public IGraphicsShaderDataBuffer<T> Buffer { get; }

    /// <summary>Gets the first logical element.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the element count.</summary>
    public ulong Count { get; }
}
