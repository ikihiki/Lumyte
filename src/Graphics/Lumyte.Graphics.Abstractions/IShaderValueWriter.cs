namespace Lumyte.Graphics.Abstractions;

/// <summary>Copies a complete unmanaged wire value and records opaque references.</summary>
public interface IShaderValueWriter
{
    /// <summary>Copies the complete C# representation, including padding, exactly once.</summary>
    /// <typeparam name="T">The unmanaged C# wire type.</typeparam>
    /// <param name="value">The complete wire value.</param>
    void WriteBytes<T>(in T value)
        where T : unmanaged;

    /// <summary>Records a reference and its C# wire slot offset (always 16 bytes).</summary>
    /// <typeparam name="T">The referenced element type.</typeparam>
    /// <param name="path">The logical reference name.</param>
    /// <param name="offset">The C# wire slot offset.</param>
    /// <param name="value">The non-owning reference.</param>
    void WriteReference<T>(string path, int offset, IGpuRef<T>? value);
}
