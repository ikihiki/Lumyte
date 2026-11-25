using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Real native boundary enforcement beneath the existing validation domain.</summary>
public sealed partial class GenericGpuNativeTests
{
    private sealed class BoundedNativeDevice(IGraphicDevice raw) : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost { add => raw.DeviceLost += value; remove => raw.DeviceLost -= value; }

        public GraphicsDeviceStatus Status => raw.Status;

        public GraphicsDeviceLoss? Loss => raw.Loss;

        public DeviceCaps Caps { get; } = raw.Caps with
        {
            MaxStorageBufferBindingSize = 65536,
            MaxStorageBuffersPerStage = 7,
            ArgumentTableBindingLimits = raw.Caps.ArgumentTableBindingLimits with { MaximumBufferRangeBytes = 65536, BuffersPerDraw = 6 },
        };

        public IGraphicsQueue Queue => new BoundedQueue(raw.Queue);

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
            => raw.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged
            => raw.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
            => raw.CreateBuffer<T>(desc);

        public IGraphicsTexture CreateTexture(TextureDesc desc)
            => raw.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc)
            => raw.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
            => new BoundedTable(raw.CreateArgumentTable(desc));

        public IGraphicsShader CreateShader(ShaderArtifact artifact)
            => raw.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore()
            => raw.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
            => new BoundedCommands(raw.CreateCommandBuffer(desc));

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
            => raw.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
            => raw.CreateGraphicsPipeline(desc);

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc)
            => raw.CreateMeshPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
            => raw.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments
            => raw.CreateShaderDataBuffer<T>(artifact, count, memory);
    }

    private sealed class BoundedTable(IArgumentTable raw) : IArgumentTable
    {
        private readonly Dictionary<uint, (object Resource, ulong Offset, ulong Size, ShaderBufferAccess Access)> _ranges = [];
        private bool _sealed;

        public uint BufferCapacity => raw.BufferCapacity;

        public uint TextureCapacity => raw.TextureCapacity;

        public uint SamplerCapacity => raw.SamplerCapacity;

        internal IArgumentTable Raw => raw;

        public void Dispose() => raw.Dispose();

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
            => raw.WriteTexture(slot, view);

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler)
            => raw.WriteSampler(slot, sampler);

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
            where T : unmanaged
        {
            Admit(slot, range.Buffer, range.OffsetInBytes, range.SizeInBytes, access);
            return raw.WriteBuffer(slot, range, access);
        }

        public void ReleaseTexture(uint slot)
            => raw.ReleaseTexture(slot);

        public void ReleaseSampler(uint slot)
            => raw.ReleaseSampler(slot);

        public void ReleaseBuffer(uint slot)
            => raw.ReleaseBuffer(slot);

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderArguments
        {
            Admit(slot, range.Buffer, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes, ShaderBufferAccess.ReadOnly);
            return raw.WriteBuffer(slot, range);
        }

        internal void Seal() => _sealed = true;

        private void Admit(uint slot, object resource, ulong offset, ulong size, ShaderBufferAccess access)
        {
            if (_sealed || size == 0 || size > 65536)
            {
                throw new NotSupportedException("Immutable table or native 64KiB storage range constraint.");
            }

            IEnumerable<(object Resource, ulong Offset, ulong Size, ShaderBufferAccess Access)> next = _ranges.Where(pair => pair.Key != slot).Select(pair => pair.Value).Append((resource, offset, size, access));
            if (next.Distinct().Count() + 1 > 7)
            {
                throw new NotSupportedException("Native physical storage ranges plus remap exceed seven.");
            }

            _ranges[slot] = (resource, offset, size, access);
        }
    }

    private sealed class BoundedCommands(IGraphicsCommandBuffer raw) : IGraphicsCommandBuffer, IGraphicsNativeOrigin
    {
        public CommandBufferState State => raw.State;

        public IGraphicDevice Device => ((IGraphicsNativeOrigin)raw).Device;

        public object AllocationIdentity => ((IGraphicsNativeOrigin)raw).AllocationIdentity;

        internal IGraphicsCommandBuffer Raw => raw;

        public void ValidateOrigin() => ((IGraphicsNativeOrigin)raw).ValidateOrigin();

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged
            => raw.CopyBuffer<TSource, TDestination>(source, destination);

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
            => raw.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
            => raw.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
            => raw.CopyTextureToBuffer(source, destination);

        public void Barrier(MemoryBarrierDesc barrier)
            => raw.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged
            => raw.Barrier<T>(barrier);

        public void Barrier(TextureBarrierDesc barrier)
            => raw.Barrier(barrier);

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
            => new BoundedRender(raw.BeginRenderPass(desc));

        public IComputeEncoder BeginComputePass(ComputePassDesc desc)
            => new BoundedCompute(raw.BeginComputePass(desc));

        public void Dispose() => raw.Dispose();

        public void Finish()
            => raw.Finish();

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : struct, IShaderArguments
            => raw.CopyBuffer<T>(source, destination);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : struct, IShaderArguments
            => raw.Barrier<T>(barrier);
    }

    private sealed class BoundedQueue(IGraphicsQueue raw) : IGraphicsQueue
    {
        public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers)
            => raw.Submit(commandBuffers.Select(command => ((BoundedCommands)command).Raw).ToArray());

        public IGraphicsSubmission Submit(QueueSubmitDesc desc)
            => raw.Submit(desc with { CommandBuffers = desc.CommandBuffers.Select(command => ((BoundedCommands)command).Raw).ToArray() });
    }

    private sealed class BoundedCompute(IComputeEncoder raw) : IComputeEncoder
    {
        public void SetArgumentTable(IArgumentTable table)
        {
            var bounded = (BoundedTable)table;
            bounded.Seal();
            raw.SetArgumentTable(bounded.Raw);
        }

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
            => raw.SetArgumentValidation(prepare);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments
            => raw.SetArguments<T>(in arguments);

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
            => raw.SetArguments<T>(in arguments, rootParameter, shaderTypeName);

        public void SetPipeline(IGraphicsComputePipeline pipeline)
            => raw.SetPipeline(pipeline);

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
            => raw.Dispatch(groupCountX, groupCountY, groupCountZ);

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
            => raw.DispatchIndirect(arguments);

        public void End()
            => raw.End();
    }

    private sealed class BoundedRender(IRenderEncoder raw) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table)
        {
            var bounded = (BoundedTable)table;
            bounded.Seal();
            raw.SetArgumentTable(bounded.Raw);
        }

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
            => raw.SetArgumentValidation(prepare);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments
            => raw.SetArguments<T>(in arguments);

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
            => raw.SetArguments<T>(in arguments, rootParameter, shaderTypeName);

        public void SetPipeline(IGraphicsPipeline pipeline)
            => raw.SetPipeline(pipeline);

        public void SetPipeline(IMeshPipeline pipeline)
            => raw.SetPipeline(pipeline);

        public void DrawMeshTasks(uint x, uint y = 1, uint z = 1)
            => raw.DrawMeshTasks(x, y, z);

        public void SetRenderState(GraphicsRenderStateDesc state)
            => raw.SetRenderState(state);

        public void SetViewport(Viewport viewport)
            => raw.SetViewport(viewport);

        public void SetScissor(ScissorRect scissor)
            => raw.SetScissor(scissor);

        public void SetBlendConstant(BlendConstant value)
            => raw.SetBlendConstant(value);

        public void SetStencilReference(uint reference)
            => raw.SetStencilReference(reference);

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
            => raw.Draw(vertexCount, instanceCount, firstVertex, firstInstance);

        public void SetIndexBuffer(BufferSlice<ushort> indices)
            => raw.SetIndexBuffer(indices);

        public void SetIndexBuffer(BufferSlice<uint> indices)
            => raw.SetIndexBuffer(indices);

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
            => raw.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
            => raw.DrawIndirect(arguments);

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
            => raw.DrawIndexedIndirect(arguments);

        public void End()
            => raw.End();
    }
}
