namespace Lumyte.Graphics;

/// <summary>
/// Serializes caller-defined logical buffer elements through a backend-validated writer.
/// </summary>
/// <typeparam name="T">The caller-defined logical element type, which may contain opaque resource references.</typeparam>
public interface IShaderDataSerializer<T>
{
    /// <summary>
    /// Writes one element using names from its Slang wire schema. The writer is valid only during this call.
    /// </summary>
    /// <param name="value">The logical element to serialize; the library does not retain it.</param>
    /// <param name="writer">The scoped writer that validates reflected field types and resolves resource references.</param>
    void Serialize(in T value, IShaderDataWriter writer);
}
