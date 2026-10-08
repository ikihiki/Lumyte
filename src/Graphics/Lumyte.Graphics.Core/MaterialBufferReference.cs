namespace Lumyte.Graphics;

/// <summary>
/// Represents a non-owning, completed material upload and its retained resource set.
/// </summary>
public readonly struct MaterialBufferReference
{
    internal MaterialBufferReference(object handle)
    {
        Handle = handle;
    }

    internal object? Handle { get; }
}
