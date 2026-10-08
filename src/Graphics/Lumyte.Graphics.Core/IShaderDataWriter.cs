namespace Lumyte.Graphics;

/// <summary>
/// Writes reflected numeric fields and opaque resource references without defining buffer contents in Core.
/// </summary>
public interface IShaderDataWriter
{
    /// <summary>
    /// Writes a numeric field with an exact reflected type. Initially supports float, int, uint and float vectors.
    /// </summary>
    /// <typeparam name="TValue">The unmanaged numeric field type; float vectors use System.Numerics types.</typeparam>
    /// <param name="fieldName">The top-level field name in the caller's Slang buffer element.</param>
    /// <param name="value">The field value. Floating-point components must be finite.</param>
    void Write<TValue>(string fieldName, TValue value)
        where TValue : unmanaged;

    /// <summary>Writes a stable opaque texture reference and records this element's dependency.</summary>
    /// <param name="fieldName">The reflected UInt32 resource field.</param>
    /// <param name="reference">The logical texture reference; null represents an absent resource.</param>
    void WriteTextureReference(string fieldName, TextureDescriptorReference? reference);

    /// <summary>Writes a stable sampler reference independently of texture references.</summary>
    /// <param name="fieldName">The reflected UInt32 resource field.</param>
    /// <param name="reference">The logical sampler reference; null represents an absent resource.</param>
    void WriteSamplerReference(string fieldName, SamplerDescriptorReference? reference);

    /// <summary>Writes a read-only buffer descriptor and records this element's dependency.</summary>
    /// <typeparam name="T">The registered unmanaged element type.</typeparam>
    /// <param name="fieldName">The reflected UInt32 resource field.</param>
    /// <param name="reference">The logical buffer registration.</param>
    void WriteBufferReference<T>(string fieldName, BufferDescriptorReference<T> reference)
        where T : unmanaged;
}
