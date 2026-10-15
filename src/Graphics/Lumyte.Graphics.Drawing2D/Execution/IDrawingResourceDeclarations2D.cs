using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes externally shared batch resources without depending on a RenderGraph implementation.</summary>
public interface IDrawingResourceDeclarations2D
{
    /// <summary>Declares a texture's entry and exit state and all external accesses.</summary>
    /// <param name="texture">The borrowed allocation.</param>
    /// <param name="state">The state restored after internal commands.</param>
    /// <param name="scope">The accesses made during the batch.</param>
    void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope);

    /// <summary>Declares a buffer's external accesses.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="scope">The accesses made during the batch.</param>
    void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged;

    /// <summary>Declares a shader data buffer's external accesses.</summary>
    /// <typeparam name="T">The shader data type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="scope">The accesses made during the batch.</param>
    void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData;
}
