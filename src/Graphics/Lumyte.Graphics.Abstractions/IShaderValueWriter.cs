namespace Lumyte.Graphics.Abstractions;

/// <summary>Synchronously consumes consecutive raw wire ranges and opaque reference slots.</summary>
public interface IShaderValueWriter
{
    /// <summary>Copies a borrowed raw range, including padding, at the current position.</summary>
    /// <param name="value">The bytes, which must not be retained beyond this call.</param>
    void WriteValue(ReadOnlySpan<byte> value);

    /// <summary>Consumes one opaque 16-byte reference slot at the current position.</summary>
    /// <typeparam name="T">The referenced element type.</typeparam>
    /// <param name="value">The non-owning reference.</param>
    void WriteReference<T>(IGpuRef<T>? value);
}
