using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerEncoder : IRenderEncoder, IComputeEncoder
{
    private ShaderTargetData? _target;
    private IArgumentTable? _table;

    internal List<ShaderValueSnapshot> Snapshots { get; } = [];

    internal List<ShaderValueSnapshot> Executions { get; } = [];

    internal List<ShaderBindingSnapshot> Bindings { get; } = [];

    internal Func<IShaderDataSource, ulong, ShaderValueSnapshot>? ReadShaderData { get; set; }

    internal int Draws { get; private set; }

    internal int IndexedDraws { get; private set; }

    internal int Dispatches { get; private set; }

    internal int Ends { get; private set; }

    internal Exception? SetArgumentsException { get; set; }

    internal Exception? EndException { get; init; }

    public void SetArgumentTable(IArgumentTable table) => _table = table;

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public void SetArguments(ShaderValueSnapshot arguments)
    {
        if (SetArgumentsException is { } error)
        {
            throw error;
        }

        ShaderDataLayout.Root(_target!, arguments.RootParameter).ValidateArguments(arguments, validateReferences: true);
        Snapshots.Add(arguments.Copy());
    }

    public void SetPipeline(IGraphicsPipeline pipeline) => _target = pipeline.Desc.VertexShader.Artifact.GetTarget(ShaderTarget.SpirV);

    public void SetPipeline(IGraphicsComputePipeline pipeline) => _target = pipeline.Desc.ComputeShader.Artifact.GetTarget(ShaderTarget.SpirV);

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

    public void SetIndexBuffer(BufferSlice<ushort> indices)
    {
    }

    public void SetIndexBuffer(BufferSlice<uint> indices)
    {
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (Snapshots.Count != 0)
        {
            CaptureExecution();
        }

        Draws++;
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        CaptureExecution();
        IndexedDraws++;
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        CaptureExecution();
        Draws++;
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        CaptureExecution();
        IndexedDraws++;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        CaptureExecution();
        Dispatches++;
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        CaptureExecution();
        Dispatches++;
    }

    public void End()
    {
        Ends++;
        if (EndException is { } error)
        {
            throw error;
        }
    }

    private void CaptureExecution()
    {
        if (Snapshots.Count != 0)
        {
            Bindings.Add(ShaderBindingSnapshot.Capture(Snapshots.Last(), _table, ReadShaderData));
            Executions.Add(Snapshots.Last());
        }
    }
}
