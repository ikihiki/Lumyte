using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing;

/// <summary>Provides graph-local declarations for a borrowed mesh, preserving the original index width.</summary>
public sealed class ModelMeshGraphResources
{
    private readonly Action<RenderGraphPass> _useIndices;

    internal ModelMeshGraphResources(RenderGraphBuffer<ModelVertex> vertices, Action<RenderGraphPass> useIndices)
    {
        Vertices = vertices;
        _useIndices = useIndices;
    }

    /// <summary>Gets the graph-local raw vertex buffer.</summary>
    public RenderGraphBuffer<ModelVertex> Vertices { get; }

    /// <summary>Declares the vertex and index reads of a model draw pass.</summary>
    /// <param name="pass">The pass in the mesh's parent graph.</param>
    /// <returns>The same pass for texture, attachment and other declarations.</returns>
    public RenderGraphPass Use(RenderGraphPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        pass.UseBuffer(Vertices, new(PipelineStage.VertexShader, ResourceAccess.ShaderRead));
        _useIndices(pass);
        return pass;
    }
}
