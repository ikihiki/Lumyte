namespace Lumyte.Graphics;

/// <summary>
/// Identifies a logical descriptor registration without exposing addresses or physical bindings.
/// </summary>
/// <typeparam name="T">The registered unmanaged buffer element type.</typeparam>
public readonly struct BufferDescriptorReference<T>
    where T : unmanaged
{
    internal BufferDescriptorReference(object handle)
    {
        Handle = handle;
    }

    internal object? Handle { get; }
}
