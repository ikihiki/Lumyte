using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class ExecutionReviewRender : IRenderEncoder
{
    internal int Draws { get; private set; }

    public void SetArgumentTable(IArgumentTable table)
    {
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
    }

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
    }

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
    }

    public void SetPipeline(IMeshPipeline pipeline) => throw new NotSupportedException();

    public void DrawMeshTasks(uint x, uint y = 1, uint z = 1) => throw new NotSupportedException();

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
    }

    public void SetViewport(Viewport viewport)
    {
    }

    public void SetScissor(ScissorRect scissor)
    {
    }

    public void SetBlendConstant(BlendConstant value)
    {
    }

    public void SetStencilReference(uint reference)
    {
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => Draws++;

    public void SetIndexBuffer(BufferSlice<ushort> indices)
    {
    }

    public void SetIndexBuffer(BufferSlice<uint> indices)
    {
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => Draws++;

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => Draws++;

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => Draws++;

    public void End()
    {
    }
}
