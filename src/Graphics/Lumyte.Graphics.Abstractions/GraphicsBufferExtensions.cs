namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides explicit range units and consistent mapping helpers without GPU work.</summary>
public static class GraphicsBufferExtensions
{
    /// <summary>Creates a non-owning raw buffer range using element units.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The owning allocation.</param>
    /// <param name="elementOffset">The first element.</param>
    /// <param name="elementCount">The positive element count.</param>
    /// <returns>The non-owning range.</returns>
    public static BufferSlice<T> SliceElements<T>(this IGraphicsBuffer<T> buffer, ulong elementOffset, ulong elementCount)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.Slice(elementOffset, elementCount);
    }

    /// <summary>Creates a non-owning shader data range using element units.</summary>
    /// <typeparam name="T">The application shader data type.</typeparam>
    /// <param name="buffer">The owning allocation.</param>
    /// <param name="elementOffset">The first logical element.</param>
    /// <param name="elementCount">The positive logical element count.</param>
    /// <returns>The non-owning range.</returns>
    public static ShaderDataSlice<T> Slice<T>(this IGraphicsShaderDataBuffer<T> buffer, ulong elementOffset, ulong elementCount)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.SliceElements(elementOffset, elementCount);
    }

    /// <summary>Rejects empty, overflowing or out-of-bounds byte ranges.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The owning allocation.</param>
    /// <param name="byteOffset">The first byte.</param>
    /// <param name="byteLength">The positive byte count.</param>
    public static void ValidateByteRange<T>(this IGraphicsBuffer<T> buffer, ulong byteOffset, ulong byteLength)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.ValidateRange(byteOffset, byteLength);
    }

    /// <summary>Copies bytes into an explicitly mapped Upload buffer without GPU work.</summary>
    /// <typeparam name="T">The unmanaged allocation element type.</typeparam>
    /// <param name="buffer">The mapped allocation.</param>
    /// <param name="source">The bytes, which must fit the selected range.</param>
    /// <param name="byteOffset">The first destination byte.</param>
    /// <param name="byteLength">The destination range length in bytes.</param>
    public static void CopyBytesFrom<T>(this IGraphicsBuffer<T> buffer, ReadOnlySpan<byte> source, ulong byteOffset, ulong byteLength)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.CopyFrom(source, byteOffset, byteLength);
    }

    /// <summary>Copies bytes from an explicitly mapped Readback buffer without GPU work or waiting.</summary>
    /// <typeparam name="T">The unmanaged allocation element type.</typeparam>
    /// <param name="buffer">The mapped allocation.</param>
    /// <param name="destination">The storage, which must fit the complete selected range.</param>
    /// <param name="byteOffset">The first source byte.</param>
    /// <param name="byteLength">The source range length in bytes.</param>
    public static void CopyBytesTo<T>(this IGraphicsBuffer<T> buffer, Span<byte> destination, ulong byteOffset, ulong byteLength)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.CopyTo(destination, byteOffset, byteLength);
    }

    /// <summary>Maps shader upload staging; cancellation releases a successful mapping after native completion.</summary>
    /// <typeparam name="T">The application shader data type.</typeparam>
    /// <param name="buffer">The upload staging allocation.</param>
    /// <param name="cancellationToken">Cancels retaining the mapping, not the native operation.</param>
    /// <returns>Completion when mapping is available, or cancellation after unmapping.</returns>
    public static async ValueTask MapAsync<T>(this IGraphicsShaderDataBuffer<T> buffer, CancellationToken cancellationToken)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        await buffer.MapAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            buffer.Unmap();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
