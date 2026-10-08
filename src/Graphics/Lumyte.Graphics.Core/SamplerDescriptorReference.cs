namespace Lumyte.Graphics;

/// <summary>
/// Identifies a logical descriptor registration without exposing addresses or physical bindings.
/// </summary>
public readonly struct SamplerDescriptorReference
{
    internal SamplerDescriptorReference(object handle)
    {
        Handle = handle;
    }

    internal object? Handle { get; }
}
