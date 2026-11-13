namespace Lumyte.Graphics.Abstractions;

/// <summary>Synchronously copies raw wire ranges and encodes references into a private destination.</summary>
/// <remarks>Implementations must validate destination bounds and must not retain source spans.
/// Raw writes must not alias the source wire. Reference writes encode exactly 16 bytes inline
/// and append opaque identity metadata without interpreting numeric fields.</remarks>
public interface IShaderWireSink
{
    /// <summary>Copies a raw range, including any padding, to its wire offset.</summary>
    /// <param name="source">The borrowed raw range.</param>
    /// <param name="offset">The destination wire offset.</param>
    void WriteRaw(ReadOnlySpan<byte> source, int offset);

    /// <summary>Encodes one backend-native 16-byte reference slot and records its opaque identity.</summary>
    /// <typeparam name="T">The referenced element type.</typeparam>
    /// <param name="offset">The destination wire offset.</param>
    /// <param name="value">The non-owning reference.</param>
    void WriteReference<T>(int offset, IGpuRef<T>? value);
}
