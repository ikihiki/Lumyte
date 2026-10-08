namespace Lumyte.Graphics;

/// <summary>
/// Specifies permitted buffer operations; combine flags at allocation.
/// </summary>
[Flags]
public enum BufferUsage
{
    /// <summary>
    /// Allows the buffer to supply GPU copy data.
    /// </summary>
    CopySource = 1,

    /// <summary>
    /// Allows GPU copies to write the buffer.
    /// </summary>
    CopyDestination = 2,

    /// <summary>
    /// Allows shaders to read buffer contents.
    /// </summary>
    ShaderRead = 4,

    /// <summary>
    /// Allows shaders to write buffer contents.
    /// </summary>
    ShaderWrite = 8,

    /// <summary>
    /// Allows use as an index buffer.
    /// </summary>
    Index = 16,
}
