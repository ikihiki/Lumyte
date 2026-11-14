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
        where T : IShaderArguments;

    /// <summary>Declares one exact sampled view; default adapters retain allocation-wide access semantics.</summary>
    /// <param name="view">The sampled view.</param>
    /// <param name="scope">The shader-read scope.</param>
    void UseTextureView(IGraphicsTextureView view, BarrierScope scope) => UseTexture(view.Texture, TextureState.Sampled, scope);

    /// <summary>Declares a borrowed immutable sampler.</summary>
    /// <param name="sampler">The sampled immutable state.</param>
    void UseSampler(IGraphicsSampler sampler)
    {
    }

    /// <summary>Declares an exact raw binding range.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="range">The binding range.</param>
    /// <param name="scope">The actual accesses.</param>
    void UseBuffer<T>(BufferSlice<T> range, BarrierScope scope)
        where T : unmanaged => UseBuffer(range.Buffer, scope);

    /// <summary>Declares an exact shader-data binding range.</summary>
    /// <typeparam name="T">The shader-data element type.</typeparam>
    /// <param name="range">The binding range.</param>
    /// <param name="scope">The actual accesses.</param>
    void UseShaderDataBuffer<T>(ShaderDataSlice<T> range, BarrierScope scope)
        where T : IShaderArguments => UseShaderDataBuffer(range.Buffer, scope);
}
