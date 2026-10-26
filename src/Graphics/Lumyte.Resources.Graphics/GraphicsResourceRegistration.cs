namespace Lumyte.Resources.Graphics;

/// <summary>Freezes a key and its definition when the resource manager's stages are constructed.</summary>
/// <typeparam name="T">The owned native output.</typeparam>
/// <param name="key">The logical resource key.</param>
/// <param name="definition">The immutable reconstruction definition.</param>
public sealed class GraphicsResourceRegistration<T>(ResourceKey<GraphicsResource<T>> key, GraphicsResourceDefinition<T> definition)
    where T : class, IDisposable
{
    /// <summary>Gets the logical key.</summary>
    public ResourceKey<GraphicsResource<T>> Key { get; } = key;

    /// <summary>Gets the reconstruction definition.</summary>
    public GraphicsResourceDefinition<T> Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));
}
