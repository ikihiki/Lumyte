namespace Lumyte.Graphics.Abstractions;

/// <summary>Connects generated application codecs to backend serializers without reflection.</summary>
public interface IShaderValueWriter
{
    /// <summary>Writes a supported numeric member.</summary>
    /// <typeparam name="T">The numeric member type.</typeparam>
    /// <param name="path">The logical member path.</param>
    /// <param name="value">The numeric value.</param>
    void WriteValue<T>(string path, in T value)
        where T : unmanaged;

    /// <summary>Writes an opaque reference member without exposing its native representation.</summary>
    /// <typeparam name="T">The referenced logical element type.</typeparam>
    /// <param name="path">The logical member path.</param>
    /// <param name="value">The registration reference.</param>
    void WriteReference<T>(string path, IGpuRef<T>? value);
}
