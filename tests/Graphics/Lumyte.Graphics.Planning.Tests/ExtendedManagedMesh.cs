using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal sealed class ExtendedManagedMesh : IMeshPipeline
{
    public MeshPipelineDesc Desc => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
