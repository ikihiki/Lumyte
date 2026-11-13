namespace Lumyte.Graphics.Abstractions;

/// <summary>Consumes opaque references at their actual C# wire offsets.</summary>
public interface IShaderReferenceCursor
{
    /// <summary>Consumes one 16-byte reference slot.</summary>
    /// <typeparam name="T">The referenced element type.</typeparam>
    /// <param name="offset">The C# wire slot offset.</param>
    /// <param name="value">The non-owning reference.</param>
    void WriteReference<T>(int offset, IGpuRef<T>? value);
}
