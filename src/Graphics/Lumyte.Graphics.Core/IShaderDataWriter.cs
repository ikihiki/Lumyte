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

    /// <summary>
    /// Resolves a sampled pair into a reflected UInt32 field. Null selects the supplied fallback.
    /// </summary>
    /// <param name="fieldName">The UInt32 field consumed by the backend sampling helper.</param>
    /// <param name="reference">The opaque sampled pair, or null for fallback; no selector value is returned.</param>
    void WriteSampledTexture2D(string fieldName, SampledTexture2DReference? reference);
}
