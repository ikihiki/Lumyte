namespace Lumyte.Graphics;

/// <summary>
/// Represents a non-owning sampled pair without exposing selectors or native handles.
/// </summary>
public readonly struct SampledTexture2DReference
{
    internal SampledTexture2DReference(object handle)
    {
        Handle = handle;
    }

    internal object? Handle { get; }
}
