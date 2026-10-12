using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns one prepared command batch that records directly to a graphics command buffer.</summary>
public interface IPreparedCommandBatch2D : IDisposable
{
    /// <summary>Declares externally shared resources; batch-private resources are synchronized internally.</summary>
    /// <param name="resources">The resource declaration adapter.</param>
    void DeclareResources(IDrawingResourceDeclarations2D resources);

    /// <summary>Records once, including any internal upload, compute and render commands.</summary>
    /// <param name="commands">The borrowed command buffer, outside an active pass.</param>
    /// <param name="target">The borrowed target in ColorAttachment state.</param>
    void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target);
}
