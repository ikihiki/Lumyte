namespace Lumyte.Graphics.RenderGraph.Common;

/// <summary>
/// Bufferless geometry arguments. The vertex shader derives geometry from the vertex ID and may
/// use material textures or root data for additional inputs.
/// </summary>
public readonly record struct ProceduralGeometry(uint VertexCount, uint InstanceCount = 1)
{
    public ProceduralGeometry Validate()
    {
        if (VertexCount == 0 || InstanceCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VertexCount));
        }
        return this;
    }
}
