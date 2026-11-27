using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedCommands(ManagedDevice device) : IGraphicsCommandBuffer, IComputeEncoder, IRenderEncoder
{
    private readonly ManagedNativePreparations _nativePreparations = new();

    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    internal IGraphicsComputePipeline? ComputePipeline { get; private set; }

    internal IGraphicsPipeline? RasterPipeline { get; private set; }

    internal RenderPassDesc? RenderPass { get; private set; }

    internal List<MemoryBarrierDesc> Barriers { get; } = [];

    internal List<TextureBarrierDesc> TextureBarriers { get; } = [];

    internal List<byte> ArgumentBytes { get; } = [];

    internal List<object> References { get; } = [];

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
        => device.Events.Add($"copy:{source.Offset}:{destination.Offset}:{source.Count}");

    public void Barrier(MemoryBarrierDesc barrier)
    {
        Barriers.Add(barrier);
        device.Events.Add("barrier");
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        device.Events.Add("compute");
        return this;
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        RenderPass = desc;
        device.Events.Add("render");
        return this;
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline) => ComputePipeline = pipeline;

    public void SetPipeline(IGraphicsPipeline pipeline) => RasterPipeline = pipeline;

    public void SetArgumentTable(IArgumentTable table) => device.Events.Add("bind-table");

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
    {
        _nativePreparations.Prepare(arguments);
        device.Events.Add("prepare-native-render");
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        _nativePreparations.Prepare(arguments);
        device.Events.Add("prepare-native-compute");
    }

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        _nativePreparations.Prepare(arguments);
        device.Events.Add("prepare-native-mesh");
    }

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        (byte[] Bytes, object[] References) captured = _nativePreparations.Consume(arguments);
        ArgumentBytes.AddRange(captured.Bytes);
        References.AddRange(captured.References);
        device.Events.Add("arguments");
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => device.Events.Add($"dispatch:{groupCountX}:{groupCountY}:{groupCountZ}");

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => device.Events.Add($"draw:{vertexCount}:{instanceCount}");

    public void End() => device.Events.Add("end");

    public void Finish()
    {
        State = CommandBufferState.Executable;
        device.Events.Add("finish");
    }

    public void Dispose()
    {
        device.Events.Add("dispose-commands-attempt");
        if (device.CommandDisposeFailures > 0)
        {
            device.CommandDisposeFailures--;
            throw new InvalidOperationException("Cleanup failed.");
        }

        device.Events.Add("dispose-commands");
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => throw new NotSupportedException();

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
        => throw new NotSupportedException();

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
        => Barrier(new MemoryBarrierDesc { Before = barrier.Before, After = barrier.After });

    public void Barrier(TextureBarrierDesc barrier)
    {
        TextureBarriers.Add(barrier);
        Barrier(new MemoryBarrierDesc { Before = barrier.Before, After = barrier.After });
    }

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments
    {
        if (source.Buffer.IsMapped || destination.Buffer.IsMapped || source.Count != destination.Count || destination.Buffer.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("A recorded shader-data copy must use unmapped, compatible typed storage.");
        }

        device.Events.Add($"copy-shaderdata:{source.Offset}:{destination.Offset}:{source.Count}");
    }

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments
        => Barrier(new MemoryBarrierDesc { Before = barrier.Before, After = barrier.After });

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException();

    public void SetPipeline(IMeshPipeline pipeline) => throw new NotSupportedException();

    public void DrawMeshTasks(uint x, uint y = 1, uint z = 1) => throw new NotSupportedException();

    public void SetRenderState(GraphicsRenderStateDesc state) => device.Events.Add("render-state");

    public void SetViewport(Viewport viewport) => device.Events.Add("viewport");

    public void SetScissor(ScissorRect scissor) => device.Events.Add("scissor");

    public void SetBlendConstant(BlendConstant value) => device.Events.Add("blend-state");

    public void SetStencilReference(uint reference) => device.Events.Add("stencil-state");

    public void SetIndexBuffer(BufferSlice<ushort> indices) => throw new NotSupportedException();

    public void SetIndexBuffer(BufferSlice<uint> indices) => throw new NotSupportedException();

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => throw new NotSupportedException();

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => throw new NotSupportedException();

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => throw new NotSupportedException();

    internal void Complete() => State = CommandBufferState.Completed;
}
