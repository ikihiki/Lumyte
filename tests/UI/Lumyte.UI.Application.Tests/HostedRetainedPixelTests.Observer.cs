using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Native forwarding observers for host strategy selection.</summary>
public sealed partial class HostedRetainedPixelTests
{
    private sealed class ObservedDevice : IGraphicDevice
    {
        private readonly IGraphicDevice _native;

        internal ObservedDevice(IGraphicDevice native)
        {
            _native = native;
            Caps = native.Caps;
        }

        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost { add => _native.DeviceLost += value; remove => _native.DeviceLost -= value; }

        public GraphicsDeviceStatus Status => _native.Status;

        public GraphicsDeviceLoss? Loss => _native.Loss;

        public DeviceCaps Caps { get; }

        public IGraphicsQueue Queue => new ObservedQueue(_native.Queue);

        internal int Dispatches { get; set; }

        internal ulong PayloadCopyBytes { get; set; }

        internal List<IShaderReference> RecordReferences { get; } = [];

        internal List<(ArgumentTableDesc Desc, IArgumentTable Table)> Tables { get; } = [];

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => _native.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => _native.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => _native.CreateBuffer(desc);

        public IGraphicsTexture CreateTexture(TextureDesc desc) => _native.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => _native.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
        {
            IArgumentTable table = _native.CreateArgumentTable(desc);
            Tables.Add((desc, table));
            return table;
        }

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => _native.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => _native.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new ObservedCommands(_native.CreateCommandBuffer(desc), this);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => _native.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => _native.CreateGraphicsPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => _native.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => _native.CreateShaderDataBuffer<T>(artifact, count, memory);
    }

    private sealed class ObservedCommands(IGraphicsCommandBuffer native, ObservedDevice device) : IGraphicsCommandBuffer
    {
        public CommandBufferState State => native.State;

        internal IGraphicsCommandBuffer Native => native;

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged
        {
            device.PayloadCopyBytes = checked(device.PayloadCopyBytes + source.SizeInBytes);
            native.CopyBuffer(source, destination);
        }

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => native.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => native.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => native.CopyTextureToBuffer(source, destination);

        public void Barrier(MemoryBarrierDesc barrier) => native.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => native.Barrier(barrier);

        public void Barrier(TextureBarrierDesc barrier)
        {
            if (barrier.AfterState != TextureState.Present)
            {
                native.Barrier(barrier);
            }
        }

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => new ObservedRender(native.BeginRenderPass(desc), device);

        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => new ObservedCompute(native.BeginComputePass(desc), device);

        public void Finish() => native.Finish();

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : struct, IShaderArguments
        {
            device.PayloadCopyBytes = checked(device.PayloadCopyBytes + (source.Count * source.Buffer.ShaderElementStrideInBytes));
            native.CopyBuffer(source, destination);
        }

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : struct, IShaderArguments => native.Barrier(barrier);

        public void Dispose() => native.Dispose();
    }

    private sealed class ObservedQueue(IGraphicsQueue native) : IGraphicsQueue
    {
        public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commands) => native.Submit(commands.Select(Unwrap).ToArray());

        // Texture-backed test presentation has no native acquire/present semaphore owner.
        // Commands, resources, compute dispatch and submission completion remain native.
        public IGraphicsSubmission Submit(QueueSubmitDesc desc) => native.Submit(desc with { CommandBuffers = desc.CommandBuffers.Select(Unwrap).ToArray(), WaitSemaphores = [], SignalSemaphores = [] });

        private static IGraphicsCommandBuffer Unwrap(IGraphicsCommandBuffer command) => command is ObservedCommands observed ? observed.Native : command;
    }

    private sealed class ObservedRender(IRenderEncoder native, ObservedDevice device) : IRenderEncoder
    {
        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);

        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments
        {
            if (T.ShaderTypeName == "DrawRangeArguments2D")
            {
                ShaderValueSnapshot snapshot = IShaderArguments.Capture(in arguments);
                device.RecordReferences.Add(Assert.IsAssignableFrom<IShaderReference>(snapshot.Values.Single(value => value.Offset == 0).Reference));
            }

            native.SetArguments(in arguments);
        }

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged => native.SetArguments(in arguments, rootParameter, shaderTypeName);

        public void SetPipeline(IGraphicsPipeline pipeline) => native.SetPipeline(pipeline);

        public void SetRenderState(GraphicsRenderStateDesc state) => native.SetRenderState(state);

        public void SetViewport(Viewport viewport) => native.SetViewport(viewport);

        public void SetScissor(ScissorRect scissor) => native.SetScissor(scissor);

        public void SetBlendConstant(BlendConstant value) => native.SetBlendConstant(value);

        public void SetStencilReference(uint reference) => native.SetStencilReference(reference);

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => native.Draw(vertexCount, instanceCount, firstVertex, firstInstance);

        public void SetIndexBuffer(BufferSlice<ushort> indices) => native.SetIndexBuffer(indices);

        public void SetIndexBuffer(BufferSlice<uint> indices) => native.SetIndexBuffer(indices);

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => native.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => native.DrawIndirect(arguments);

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => native.DrawIndexedIndirect(arguments);

        public void End() => native.End();
    }

    private sealed class ObservedCompute(IComputeEncoder native, ObservedDevice device) : IComputeEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments
            => native.SetArguments(in arguments);

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
            => native.SetArguments(in arguments, rootParameter, shaderTypeName);

        public void SetPipeline(IGraphicsComputePipeline pipeline) => native.SetPipeline(pipeline);

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            device.Dispatches++;
            native.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
        {
            device.Dispatches++;
            native.DispatchIndirect(arguments);
        }

        public void End() => native.End();
    }
}
