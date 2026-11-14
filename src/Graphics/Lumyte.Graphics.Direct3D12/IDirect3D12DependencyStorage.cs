using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Publishes dependency metadata for shader-data storage.</summary>
internal interface IDirect3D12DependencyStorage
{
    /// <summary>Publishes the dependencies of the stored shader data.</summary>
    /// <param name="dependencies">The dependency metadata to publish.</param>
    void PublishDependencies(IShaderReferenceMetadata dependencies);
}
