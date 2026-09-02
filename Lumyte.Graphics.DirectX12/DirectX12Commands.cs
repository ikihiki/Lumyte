using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Device
{
    private DirectX12Recorder BeginCommands()
    {
        VerifyNotDisposed();
        ComPtr<ID3D12CommandAllocator> allocator = default;
        ComPtr<ID3D12GraphicsCommandList> commands = default;
        try
        {
            SilkMarshal.ThrowHResult(device.CreateCommandAllocator<ID3D12CommandAllocator>(
                CommandListType.Direct, out allocator));
            SilkMarshal.ThrowHResult(device.CreateCommandList<
                ID3D12CommandAllocator,
                ID3D12PipelineState,
                ID3D12GraphicsCommandList>(
                    0, CommandListType.Direct, allocator, default, out commands));
            return new(this, allocator, commands);
        }
        catch
        {
            commands.Dispose();
            allocator.Dispose();
            throw;
        }
    }

    private void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, DirectX12Semaphore signal, ulong signalValue)
    {
        VerifyNotDisposed();
        if (commandBuffers.IsEmpty)
        {
            throw new ArgumentException("At least one command buffer is required.", nameof(commandBuffers));
        }
        if (signal.Owner != this)
        {
            throw new ArgumentException("Semaphore belongs to another backend.", nameof(signal));
        }
        signal.ValidateSignal(signalValue);

        var recorders = new DirectX12Recorder[commandBuffers.Length];
        for (int index = 0; index < commandBuffers.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(commandBuffers[index]);
            if (commandBuffers[index].Finish() is not DirectX12Recorder recorder || recorder.Owner != this)
            {
                throw new ArgumentException("Command buffer belongs to another backend.", nameof(commandBuffers));
            }
            recorders[index] = recorder;
            ComPtr<ID3D12CommandList> executable = recorder.Commands.QueryInterface<ID3D12CommandList>();
            try { queue.ExecuteCommandLists(1, ref executable); }
            finally { executable.Dispose(); }
        }
        SilkMarshal.ThrowHResult(queue.Signal(signal.Fence, signalValue));
        signal.Track(signalValue, recorders);
    }

    private void Wait(DirectX12Semaphore semaphore, ulong value)
    {
        VerifyNotDisposed();
        if (semaphore.Owner != this)
        {
            throw new ArgumentException("Semaphore belongs to another backend.", nameof(semaphore));
        }
        while (semaphore.Fence.GetCompletedValue() < value) { Thread.Yield(); }
        semaphore.ReleaseCompleted(value);
    }

    private void Transition(
        ComPtr<ID3D12GraphicsCommandList> commands,
        TextureRecord texture,
        ResourceStates target)
    {
        if (texture.State == target) { return; }
        ResourceTransitionBarrier transition = new(
            texture.Resource,
            D3D12.ResourceBarrierAllSubresources,
            texture.State,
            target);
        ResourceBarrier barrier = new(ResourceBarrierType.Transition, ResourceBarrierFlags.None, null, transition);
        commands.ResourceBarrier(1, in barrier);
        texture.State = target;
    }

    private ComPtr<ID3D12Resource> CreateCommittedBuffer(HeapType heapType, ulong size, ResourceStates state)
    {
        HeapProperties properties = new(heapType);
        ResourceDesc description = BufferDescription(size);
        SilkMarshal.ThrowHResult(device.CreateCommittedResource<ID3D12Resource>(
            in properties, HeapFlags.None, in description, state, null, out ComPtr<ID3D12Resource> resource));
        return resource;
    }

    private static ulong NativeRowPitch(GpuTextureCopyFootprint footprint) => Align(footprint.RowPitch, D3D12.TextureDataPitchAlignment);

    private sealed class QueueAdapter(DirectX12Device owner) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => new(owner.BeginCommands());

        public GpuSemaphore CreateSemaphore(ulong initialValue = 0)
        {
            owner.VerifyNotDisposed();
            SilkMarshal.ThrowHResult(owner.device.CreateFence<ID3D12Fence>(
                initialValue, FenceFlags.None, out ComPtr<ID3D12Fence> fence));
            return new DirectX12Semaphore(owner, fence, initialValue);
        }

        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
        {
            if (signalSemaphore is not DirectX12Semaphore native)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(signalSemaphore));
            }
            owner.Submit(commandBuffers, native, signalValue);
        }

        public void Wait(GpuSemaphore semaphore, ulong value)
        {
            if (semaphore is not DirectX12Semaphore native)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(semaphore));
            }
            owner.Wait(native, value);
        }
    }

    private sealed class DirectX12Recorder : IGpuCommandRecorder, IDisposable
    {
        private readonly List<ComPtr<ID3D12Resource>> temporaryResources = [];
        private readonly List<ComPtr<ID3D12DescriptorHeap>> temporaryDescriptorHeaps = [];
        private readonly List<Action> completionActions = [];
        private PipelineRecord? currentPipeline;
        private ComPtr<ID3D12DescriptorHeap> textureHeap;
        private ComPtr<ID3D12DescriptorHeap> samplerHeap;
        private bool hasTextureHeap;
        private bool hasSamplerHeap;
        private bool disposed;

        public DirectX12Recorder(
            DirectX12Device owner,
            ComPtr<ID3D12CommandAllocator> allocator,
            ComPtr<ID3D12GraphicsCommandList> commands)
        {
            Owner = owner;
            Allocator = allocator;
            Commands = commands;
        }

        public DirectX12Device Owner { get; }
        public ComPtr<ID3D12CommandAllocator> Allocator;
        public ComPtr<ID3D12GraphicsCommandList> Commands;

        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards)
        {
            Owner.VerifyNotDisposed();
        }

        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth)
        {
            Owner.VerifyNotDisposed();
            if (colors.Count > D3D12.SimultaneousRenderTargetCount)
            {
                throw new NotSupportedException("Direct3D 12 supports at most eight simultaneous color targets.");
            }
            CpuDescriptorHandle* colorHandles = stackalloc CpuDescriptorHandle[colors.Count];
            float* clear = stackalloc float[4];
            for (int index = 0; index < colors.Count; index++)
            {
                GpuColorAttachment attachment = colors[index];
                TextureViewRecord view = RequireView(attachment.View, DescriptorHeapType.Rtv);
                TextureRecord texture = Owner.RequireTexture(attachment.View.Texture);
                Owner.Transition(Commands, texture, ResourceStates.RenderTarget);
                colorHandles[index] = view.AttachmentHandle;
                if (attachment.LoadOperation == GpuAttachmentLoadOperation.Clear)
                {
                    clear[0] = attachment.ClearColor.Red;
                    clear[1] = attachment.ClearColor.Green;
                    clear[2] = attachment.ClearColor.Blue;
                    clear[3] = attachment.ClearColor.Alpha;
                    Commands.ClearRenderTargetView(colorHandles[index], clear, 0, (Box2D<int>*)null);
                }
            }

            CpuDescriptorHandle depthHandle = default;
            CpuDescriptorHandle* depthPointer = null;
            if (depth is { } depthAttachment)
            {
                TextureViewRecord view = RequireView(depthAttachment.View, DescriptorHeapType.Dsv);
                TextureRecord texture = Owner.RequireTexture(depthAttachment.View.Texture);
                Owner.Transition(Commands, texture, ResourceStates.DepthWrite);
                depthHandle = view.AttachmentHandle;
                depthPointer = &depthHandle;
                if (depthAttachment.LoadOperation == GpuAttachmentLoadOperation.Clear)
                {
                    GpuFormat format = depthAttachment.View.Description.Format;
                    ClearFlags flags = ClearFlags.Depth;
                    if (GpuFormatInfo.HasStencil(format)) { flags |= ClearFlags.Stencil; }
                    Commands.ClearDepthStencilView(
                        depthHandle,
                        flags,
                        depthAttachment.ClearValue.Depth,
                        depthAttachment.ClearValue.Stencil,
                        0,
                        (Box2D<int>*)null);
                }
            }
            Commands.OMSetRenderTargets(checked((uint)colors.Count), colorHandles, false, depthPointer);
        }

        public void EndRendering() { }

        public void SetPipeline(GpuRasterPipelineHandle pipeline)
        {
            if (!Owner.pipelines.TryGetValue(pipeline.Value, out PipelineRecord? record))
            {
                throw new ArgumentException("Pipeline does not belong to this Direct3D 12 device.", nameof(pipeline));
            }
            currentPipeline = record;
            Commands.SetPipelineState(record.Pipeline);
            Commands.SetGraphicsRootSignature(record.RootSignature);
            Commands.IASetPrimitiveTopology(record.Topology switch
            {
                GpuPrimitiveTopology.TriangleList => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist,
                GpuPrimitiveTopology.TriangleStrip => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglestrip,
                _ => throw new ArgumentOutOfRangeException(nameof(pipeline)),
            });
            BindDescriptorHeaps();
        }

        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor)
        {
            var nativeViewport = new Viewport(
                viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            var nativeScissor = new Box2D<int>(
                checked((int)scissor.X),
                checked((int)scissor.Y),
                checked((int)(scissor.X + scissor.Width)),
                checked((int)(scissor.Y + scissor.Height)));
            Commands.RSSetViewports(1, in nativeViewport);
            Commands.RSSetScissorRects(1, in nativeScissor);
        }

        public void Draw(uint vertexCount, uint instanceCount) =>
            Commands.DrawInstanced(vertexCount, instanceCount, 0, 0);

        public void CopyMemoryToTexture(
            GpuMemoryAddress source,
            GpuTextureHandle destination,
            GpuTextureCopyFootprint footprint)
        {
            TextureRecord texture = Owner.RequireTexture(destination);
            MemoryRecord memory = RequireMemory(source, GpuMemoryKind.HostMapped);
            ValidateFootprint(texture, footprint);
            ulong rowPitch = NativeRowPitch(footprint);
            ulong stagingSize = checked(rowPitch * footprint.Height);
            ComPtr<ID3D12Resource> staging = Owner.CreateCommittedBuffer(
                HeapType.Upload, stagingSize, ResourceStates.GenericRead);
            temporaryResources.Add(staging);
            void* mapped = null;
            SilkMarshal.ThrowHResult(staging.Map(0, (Silk.NET.Direct3D12.Range*)null, &mapped));
            try
            {
                new Span<byte>(mapped, checked((int)stagingSize)).Clear();
                for (uint row = 0; row < footprint.Height; row++)
                {
                    var sourceRow = new ReadOnlySpan<byte>(
                        (byte*)memory.CpuAddress + checked((nint)(source.Offset + row * footprint.RowPitch)),
                        checked((int)(footprint.Width * footprint.BytesPerPixel)));
                    sourceRow.CopyTo(new Span<byte>(
                        (byte*)mapped + checked((nint)(row * rowPitch)),
                        sourceRow.Length));
                }
            }
            finally
            {
                staging.Unmap(0, (Silk.NET.Direct3D12.Range*)null);
            }

            Owner.Transition(Commands, texture, ResourceStates.CopyDest);
            var placed = new PlacedSubresourceFootprint(
                0,
                new SubresourceFootprint(
                    ToDxgiFormat(texture.Description.Format),
                    footprint.Width,
                    footprint.Height,
                    1,
                    checked((uint)rowPitch)));
            var sourceLocation = new TextureCopyLocation(staging.Handle, TextureCopyType.PlacedFootprint, placedFootprint: placed);
            var destinationLocation = new TextureCopyLocation(texture.Resource.Handle, TextureCopyType.SubresourceIndex, subresourceIndex: 0);
            Commands.CopyTextureRegion(in destinationLocation, 0, 0, 0, in sourceLocation, null);
        }

        public void CopyTextureToMemory(
            GpuTextureHandle source,
            GpuMemoryAddress destination,
            GpuTextureCopyFootprint footprint)
        {
            TextureRecord texture = Owner.RequireTexture(source);
            MemoryRecord memory = RequireMemory(destination, GpuMemoryKind.HostCached);
            ValidateFootprint(texture, footprint);
            ulong rowPitch = NativeRowPitch(footprint);
            ulong stagingSize = checked(rowPitch * footprint.Height);
            ComPtr<ID3D12Resource> staging = Owner.CreateCommittedBuffer(
                HeapType.Readback, stagingSize, ResourceStates.CopyDest);
            temporaryResources.Add(staging);
            Owner.Transition(Commands, texture, ResourceStates.CopySource);
            var placed = new PlacedSubresourceFootprint(
                0,
                new SubresourceFootprint(
                    ToDxgiFormat(texture.Description.Format),
                    footprint.Width,
                    footprint.Height,
                    1,
                    checked((uint)rowPitch)));
            var destinationLocation = new TextureCopyLocation(staging.Handle, TextureCopyType.PlacedFootprint, placedFootprint: placed);
            var sourceLocation = new TextureCopyLocation(texture.Resource.Handle, TextureCopyType.SubresourceIndex, subresourceIndex: 0);
            Commands.CopyTextureRegion(in destinationLocation, 0, 0, 0, in sourceLocation, null);
            completionActions.Add(() => CopyReadback(staging, memory, destination.Offset, footprint, rowPitch, stagingSize));
        }

        public void SetResourceTable(GpuResourceTable table)
        {
            Owner.VerifyNotDisposed();
            ArgumentNullException.ThrowIfNull(table);
            if (table.TextureSlotCount > 64 || table.SamplerSlotCount > 64)
            {
                throw new NotSupportedException("Direct3D 12 raster pipelines support at most 64 logical texture and sampler slots.");
            }

            hasTextureHeap = false;
            hasSamplerHeap = false;

            if (table.TextureSlotCount != 0)
            {
                textureHeap = Owner.CreateDescriptorHeap(
                    DescriptorHeapType.CbvSrvUav, checked((uint)table.TextureSlotCount), true);
                temporaryDescriptorHeaps.Add(textureHeap);
                hasTextureHeap = true;
                for (int slot = 0; slot < table.TextureSlotCount; slot++)
                {
                    TextureId id = table.GetTexture(slot);
                    if (id.IsNull) { continue; }
                    if (!Owner.textureViews.TryGetValue(id.Value, out TextureViewRecord? view))
                    {
                        throw new ArgumentException($"Texture slot {slot} does not belong to this Direct3D 12 device.", nameof(table));
                    }
                    TextureRecord texture = Owner.RequireTexture(view.Texture);
                    Owner.Transition(Commands, texture, ResourceStates.PixelShaderResource);
                    CpuDescriptorHandle destination = Owner.Offset(
                        textureHeap.GetCPUDescriptorHandleForHeapStart(), checked((uint)slot), DescriptorHeapType.CbvSrvUav);
                    var srv = new ShaderResourceViewDesc(
                        ToDxgiFormat(view.View.Description.Format),
                        SrvDimension.Texture2D,
                        DefaultShaderComponentMapping,
                        texture2D: new Tex2DSrv(view.View.Description.BaseMip, view.View.Description.MipCount, 0, 0));
                    Owner.device.CreateShaderResourceView(texture.Resource, in srv, destination);
                }
            }

            if (table.SamplerSlotCount != 0)
            {
                samplerHeap = Owner.CreateDescriptorHeap(
                    DescriptorHeapType.Sampler, checked((uint)table.SamplerSlotCount), true);
                temporaryDescriptorHeaps.Add(samplerHeap);
                hasSamplerHeap = true;
                for (int slot = 0; slot < table.SamplerSlotCount; slot++)
                {
                    SamplerId id = table.GetSampler(slot);
                    if (id.IsNull) { continue; }
                    if (!Owner.samplers.TryGetValue(id.Value, out GpuSamplerDescription description))
                    {
                        throw new ArgumentException($"Sampler slot {slot} does not belong to this Direct3D 12 device.", nameof(table));
                    }
                    CpuDescriptorHandle destination = Owner.Offset(
                        samplerHeap.GetCPUDescriptorHandleForHeapStart(), checked((uint)slot), DescriptorHeapType.Sampler);
                    var native = new SamplerDesc(
                        ToFilter(description),
                        ToAddressMode(description.AddressU),
                        ToAddressMode(description.AddressV),
                        TextureAddressMode.Clamp,
                        0, 1, ComparisonFunc.Always, 0, float.MaxValue);
                    Owner.device.CreateSampler(in native, destination);
                }
            }
            BindDescriptorHeaps();
        }

        public void SetRootData(ReadOnlySpan<byte> data)
        {
            if (currentPipeline is null)
            {
                throw new InvalidOperationException("A raster pipeline must be bound before root data.");
            }
            fixed (byte* pointer = data)
            {
                Commands.SetGraphicsRoot32BitConstants(2, checked((uint)data.Length / 4), pointer, 0);
            }
        }

        public void End() => SilkMarshal.ThrowHResult(Commands.Close());

        public void Complete()
        {
            foreach (Action action in completionActions) { action(); }
            Dispose();
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            foreach (ComPtr<ID3D12Resource> resource in temporaryResources) { resource.Dispose(); }
            foreach (ComPtr<ID3D12DescriptorHeap> heap in temporaryDescriptorHeaps) { heap.Dispose(); }
            Commands.Dispose();
            Allocator.Dispose();
        }

        private TextureViewRecord RequireView(GpuTextureView view, DescriptorHeapType type)
        {
            if (!Owner.textureViews.TryGetValue(view.Id.Value, out TextureViewRecord? record)
                || record.View != view || record.AttachmentType != type)
            {
                throw new ArgumentException("Attachment view does not belong to this Direct3D 12 device.", nameof(view));
            }
            return record;
        }

        private MemoryRecord RequireMemory(GpuMemoryAddress address, GpuMemoryKind kind)
        {
            if (!Owner.memories.TryGetValue(address.AllocationId, out MemoryRecord? memory)
                || memory.Kind != kind || address.Offset > memory.Size || address.Length > memory.Size - address.Offset)
            {
                throw new ArgumentException("Memory address is not a compatible allocation on this device.", nameof(address));
            }
            return memory;
        }

        private static void ValidateFootprint(TextureRecord texture, GpuTextureCopyFootprint footprint)
        {
            if (footprint.Width > texture.Description.Width || footprint.Height > texture.Description.Height
                || footprint.BytesPerPixel != GpuFormatInfo.BytesPerPixel(texture.Description.Format))
            {
                throw new ArgumentException("Copy footprint is incompatible with the texture.", nameof(footprint));
            }
        }

        private void BindDescriptorHeaps()
        {
            if (currentPipeline is null) { return; }
            ID3D12DescriptorHeap** heaps = stackalloc ID3D12DescriptorHeap*[2];
            uint count = 0;
            if (hasTextureHeap) { heaps[count++] = textureHeap.Handle; }
            if (hasSamplerHeap) { heaps[count++] = samplerHeap.Handle; }
            if (count != 0) { Commands.SetDescriptorHeaps(count, heaps); }
            if (hasTextureHeap)
            {
                Commands.SetGraphicsRootDescriptorTable(0, textureHeap.GetGPUDescriptorHandleForHeapStart());
            }
            if (hasSamplerHeap)
            {
                Commands.SetGraphicsRootDescriptorTable(1, samplerHeap.GetGPUDescriptorHandleForHeapStart());
            }
        }

        private static void CopyReadback(
            ComPtr<ID3D12Resource> staging,
            MemoryRecord memory,
            ulong destinationOffset,
            GpuTextureCopyFootprint footprint,
            ulong nativeRowPitch,
            ulong stagingSize)
        {
            void* mapped = null;
            Silk.NET.Direct3D12.Range readRange = new(0, checked((nuint)stagingSize));
            SilkMarshal.ThrowHResult(staging.Map(0, &readRange, &mapped));
            try
            {
                int rowBytes = checked((int)(footprint.Width * footprint.BytesPerPixel));
                for (uint row = 0; row < footprint.Height; row++)
                {
                    var source = new ReadOnlySpan<byte>(
                        (byte*)mapped + checked((nint)(row * nativeRowPitch)), rowBytes);
                    source.CopyTo(new Span<byte>(
                        (byte*)memory.CpuAddress + checked((nint)(destinationOffset + row * footprint.RowPitch)),
                        rowBytes));
                }
            }
            finally
            {
                Silk.NET.Direct3D12.Range written = new(0, 0);
                staging.Unmap(0, &written);
            }
        }
    }

    private sealed class DirectX12Semaphore : GpuSemaphore
    {
        private readonly SortedDictionary<ulong, List<DirectX12Recorder>> pending = [];
        private ulong lastSignalValue;
        private bool disposed;

        public DirectX12Semaphore(DirectX12Device owner, ComPtr<ID3D12Fence> fence, ulong initialValue)
        {
            Owner = owner;
            Fence = fence;
            lastSignalValue = initialValue;
        }

        public DirectX12Device Owner { get; }
        public ComPtr<ID3D12Fence> Fence;

        public void ValidateSignal(ulong value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value <= lastSignalValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Signal values must increase monotonically.");
            }
        }

        public void Track(ulong value, DirectX12Recorder[] recorders)
        {
            lastSignalValue = value;
            pending.Add(value, [.. recorders]);
        }

        public void ReleaseCompleted(ulong value)
        {
            foreach (ulong key in pending.Keys.TakeWhile(key => key <= value).ToArray())
            {
                foreach (DirectX12Recorder recorder in pending[key]) { recorder.Complete(); }
                pending.Remove(key);
            }
        }

        public override void Dispose()
        {
            if (disposed) { return; }
            if (pending.Count != 0)
            {
                throw new InvalidOperationException("Semaphore still owns in-flight command buffers.");
            }
            disposed = true;
            Fence.Dispose();
        }
    }
}
