using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

/// <summary>Publishes dependency metadata for shader-data storage.</summary>
internal interface IWgpuDependencyStorage
{
    /// <summary>Publishes the dependencies of the stored shader data.</summary>
    /// <param name="dependencies">The dependency metadata to publish.</param>
    void PublishDependencies(IShaderReferenceMetadata dependencies);
}
