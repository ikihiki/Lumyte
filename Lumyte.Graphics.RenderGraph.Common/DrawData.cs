namespace Lumyte.Graphics.RenderGraph.Common;

public readonly record struct DrawData(
    DrawMaterial Material,
    ProceduralGeometry Geometry,
    DrawTransforms Transforms)
{
    public DrawData Validate()
    {
        ArgumentNullException.ThrowIfNull(Material);
        Geometry.Validate();
        return this;
    }
}
